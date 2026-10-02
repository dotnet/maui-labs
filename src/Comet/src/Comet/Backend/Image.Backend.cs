#nullable enable
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Comet.Backend;

namespace Comet
{
	// Backend property emission for Image. Merges with the `partial class Image`.
	public partial class Image
	{
		static readonly ConditionalWeakTable<ICometBackendNode, ImageLoadState> LoadStates = new();

		sealed class ImageLoadState
		{
			public object? CurrentSourceToken;
			public CancellationTokenSource? StreamLoadCancellation;
			public int SourceGeneration;
			public Microsoft.Maui.IImageSource? LoadedStreamSource;
			public byte[]? LoadedStreamBytes;
		}

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			var state = LoadStates.GetValue(node, static _ => new ImageLoadState());

			// The string source (a URL or resource name) is what the backends resolve; the
			// IImageSource path is a later step.
			var source = StringSource?.CurrentValue;
			if (!string.IsNullOrEmpty(source))
				ApplyStringSource(node, state, $"string:{source}", source);
			else
				ApplyImageSource(node, state, ImageSource?.CurrentValue);

			if (this.GetEnvironment<Microsoft.Maui.Aspect?>("Aspect") is { } aspect)
				node.ApplyProperty(PropertyIds.Image_Aspect, PropertyValue.From((int)aspect));
		}

		void ApplyImageSource(
			ICometBackendNode node,
			ImageLoadState state,
			Microsoft.Maui.IImageSource? source)
		{
			switch (source)
			{
				case Microsoft.Maui.IFontImageSource font when !string.IsNullOrEmpty(font.Glyph):
					ApplyFontSource(node, state, source, font);
					break;
				case Microsoft.Maui.IFileImageSource file when !string.IsNullOrWhiteSpace(file.File):
					ApplyStringSource(node, state, $"file:{file.File}", file.File);
					break;
				case Microsoft.Maui.IUriImageSource uri when uri.Uri is not null:
					ApplyStringSource(node, state, $"uri:{uri.Uri}", uri.Uri.ToString());
					break;
				case Microsoft.Maui.IStreamImageSource stream:
					var changed = !ReferenceEquals(state.CurrentSourceToken, source);
					if (changed)
					{
						BeginSource(state, source);
						node.ApplyProperty(PropertyIds.Image_Source, PropertyValue.From(string.Empty));
						node.ApplyProperty(PropertyIds.Image_Data, PropertyValue.FromObject(System.Array.Empty<byte>()));
					}
					if (ReferenceEquals(source, state.LoadedStreamSource) && state.LoadedStreamBytes is not null)
						node.ApplyProperty(PropertyIds.Image_Data, PropertyValue.FromObject(state.LoadedStreamBytes));
					else if (changed)
					{
						state.StreamLoadCancellation = new CancellationTokenSource();
						_ = LoadStreamAsync(
							state, source!, stream, node, state.SourceGeneration,
							state.StreamLoadCancellation.Token);
					}
					break;
				default:
					if (state.CurrentSourceToken is not null)
						BeginSource(state, null);
					node.ApplyProperty(PropertyIds.Image_Source, PropertyValue.From(string.Empty));
					node.ApplyProperty(PropertyIds.Image_Data, PropertyValue.FromObject(System.Array.Empty<byte>()));
					break;
			}
		}

		void ApplyFontSource(
			ICometBackendNode node,
			ImageLoadState state,
			Microsoft.Maui.IImageSource source,
			Microsoft.Maui.IFontImageSource fontSource)
		{
			if (!ReferenceEquals(state.CurrentSourceToken, source))
				BeginSource(state, source);

			var font = fontSource.Font;
			var registration = FontFamilyRegistry.Resolve(font.Family);
			var weight = font.Weight == Microsoft.Maui.FontWeight.Regular &&
				registration.PreferredWeight is { } registeredWeight
					? registeredWeight
					: font.Weight;

			node.ApplyProperty(PropertyIds.Image_Source, PropertyValue.From(string.Empty));
			node.ApplyProperty(PropertyIds.Image_Data, PropertyValue.FromObject(System.Array.Empty<byte>()));
			node.ApplyProperty(PropertyIds.Image_FontFamily, PropertyValue.From(registration.FaceName));
			node.ApplyProperty(PropertyIds.Image_FontSize, PropertyValue.From(font.Size > 0 ? font.Size : 24));
			node.ApplyProperty(PropertyIds.Image_FontWeight, PropertyValue.From((int)weight));
			node.ApplyProperty(
				PropertyIds.Image_FontItalic,
				PropertyValue.From(font.Slant == Microsoft.Maui.FontSlant.Italic));
			node.ApplyProperty(PropertyIds.Image_FontColor, PropertyValue.From(fontSource.Color));
			node.ApplyProperty(
				PropertyIds.Image_FontAutoScaling,
				PropertyValue.From(font.AutoScalingEnabled));

			// Glyph is deliberately last: platform nodes can rasterize once after all of the
			// typed font parameters above have reached the retained node.
			node.ApplyProperty(PropertyIds.Image_FontGlyph, PropertyValue.From(fontSource.Glyph));
		}

		void ApplyStringSource(
			ICometBackendNode node,
			ImageLoadState state,
			string token,
			string source)
		{
			if (!Equals(state.CurrentSourceToken, token))
				BeginSource(state, token);
			node.ApplyProperty(PropertyIds.Image_Data, PropertyValue.FromObject(System.Array.Empty<byte>()));
			node.ApplyProperty(PropertyIds.Image_Source, PropertyValue.From(source));
		}

		void BeginSource(ImageLoadState state, object? token)
		{
			state.StreamLoadCancellation?.Cancel();
			state.StreamLoadCancellation?.Dispose();
			state.StreamLoadCancellation = null;
			state.CurrentSourceToken = token;
			state.SourceGeneration++;
			state.LoadedStreamSource = null;
			state.LoadedStreamBytes = null;
		}

		async Task LoadStreamAsync(
			ImageLoadState state,
			Microsoft.Maui.IImageSource source,
			Microsoft.Maui.IStreamImageSource streamSource,
			ICometBackendNode node,
			int generation,
			CancellationToken cancellationToken)
		{
			try
			{
				await using var stream = await streamSource.GetStreamAsync(cancellationToken).ConfigureAwait(false);
				if (stream is null)
					return;
				using var buffer = new MemoryStream();
				await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
				var bytes = buffer.ToArray();
				if (cancellationToken.IsCancellationRequested || generation != state.SourceGeneration ||
					!ReferenceEquals(state.CurrentSourceToken, source))
					return;
				state.LoadedStreamSource = source;
				state.LoadedStreamBytes = bytes;
				ThreadHelper.RunOnMainThread(() =>
				{
					if (!cancellationToken.IsCancellationRequested && generation == state.SourceGeneration &&
						ReferenceEquals(state.CurrentSourceToken, source))
						node.ApplyProperty(PropertyIds.Image_Data, PropertyValue.FromObject(bytes));
				});
			}
			catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
			catch (System.Exception exc)
			{
				Logger.Warn("An unexpected error occurred loading an image stream.", exc);
			}
		}
	}
}
