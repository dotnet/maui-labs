using System;
using System.Collections.Generic;
using Comet.Graphics;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Comet
{
	public class Image : View, Microsoft.Maui.IImage
	{
		protected static Dictionary<string, string> ImageHandlerPropertyMapper = new(HandlerPropertyMapper)
		{
			[nameof(ImageSource)] = nameof(IImageSourcePart.Source),
		};
		public Image(IImageSource imageSource = null)
		{
			ImageSource = imageSource is not null ? new PropertySubscription<IImageSource>(imageSource) : null;
		}

		public Image(string source)
		{
			StringSource = new PropertySubscription<string>(source);
		}

		public Image(Func<IImageSource> bitmap)
		{
			ImageSource = PropertySubscription<IImageSource>.FromFunc(bitmap);
		}

		public Image(Func<string> source)
		{
			StringSource = PropertySubscription<string>.FromFunc(source);
		}

		private PropertySubscription<IImageSource> _imageSource;
		public PropertySubscription<IImageSource> ImageSource
		{
			get => _imageSource;
			private set => this.SetPropertySubscription(ref _imageSource, value);
		}

		private PropertySubscription<string> _source;
		public PropertySubscription<string> StringSource
		{
			get => _source;
			protected set
			{
				this.SetPropertySubscription(ref _source, value);
				CreateImageSource(_source.CurrentValue);
			}
		}

		public override void ViewPropertyChanged(string property, object value)
		{
			base.ViewPropertyChanged(property, value);
			if (property == nameof(StringSource))
			{
				InvalidateMeasurement();
				CreateImageSource((string)value);
			}
		}

		private void CreateImageSource(string source)
		{
			try
			{
				_imageSource ??= new PropertySubscription<IImageSource>(default(IImageSource));
				_imageSource.Set((ImageSource)source);
				ViewHandler?.UpdateValue(nameof(IImageSourcePart.Source));
			}
			catch (Exception exc)
			{
				Logger.Warn("An unexpected error occurred loading a bitmap.", exc);
			}
		}

		void IImageSourcePart.UpdateIsLoading(bool isLoading)
		{

		}

		Aspect Microsoft.Maui.IImage.Aspect => this.GetEnvironment<Aspect>(nameof(Aspect));

		bool Microsoft.Maui.IImage.IsOpaque => this.GetEnvironment<bool>(nameof(Microsoft.Maui.IImage.IsOpaque));

		IImageSource IImageSourcePart.Source => ImageSource?.CurrentValue;

		bool IImageSourcePart.IsAnimationPlaying => this.GetEnvironment<bool>(nameof(Microsoft.Maui.IImage.IsAnimationPlaying));


		protected override string GetHandlerPropertyName(string property)
			=> ImageHandlerPropertyMapper.TryGetValue(property, out var value) ? value : property;
	}
}
