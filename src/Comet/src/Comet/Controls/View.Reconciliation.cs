#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Comet.Reactive;

namespace Comet;

public partial class View
{
	static long _nextConstructionOrder;
	static long _nextDeclarativeBodyInvocation;
	readonly long _constructionOrder = System.Threading.Interlocked.Increment(
		ref _nextConstructionOrder);
	readonly object _standaloneDeclarativeIdentity = new();
	DeclarativeDeclaration? _declarativeDeclaration;
	HashSet<View>? _declarativeLifetimeChildren;
	View? _declarativeLifetimeOwner;
	long _latestDeclarativeBodyInvocation;

	internal DeclarativeRenderScope BeginDeclarativeRender()
	{
		var invocation = System.Threading.Interlocked.Increment(
			ref _nextDeclarativeBodyInvocation);
		_latestDeclarativeBodyInvocation = invocation;
		return new DeclarativeRenderScope(
			this,
			GetDeclarativeBodyIdentity(),
			invocation,
			System.Threading.Volatile.Read(ref _nextConstructionOrder));
	}

	internal bool WasFreshlyDeclaredAtSameBodyPositionAs(View previous)
	{
		if (previous is null ||
			_declarativeDeclaration is not { } current ||
			previous._declarativeDeclaration is not { } prior ||
			current.Invocation != current.Owner._latestDeclarativeBodyInvocation)
			return false;

		return current.Identity.Equals(prior.Identity);
	}

	internal virtual bool TryRetainForOwnerTransfer()
		=> false;

	DeclarativeViewIdentity GetDeclarativeBodyIdentity()
		=> (_declarativeDeclaration?.Identity
			?? new DeclarativeViewIdentity(_standaloneDeclarativeIdentity, string.Empty))
			.EnterBody(GetType());

	void TrackDeclarativeLifetimeChild(View child)
	{
		if (ReferenceEquals(child, this))
			return;

		if (!ReferenceEquals(
			child._declarativeLifetimeOwner,
			this))
		{
			child._declarativeLifetimeOwner?
				._declarativeLifetimeChildren?
				.Remove(child);
			child._declarativeLifetimeOwner = this;
		}

		_declarativeLifetimeChildren ??= new HashSet<View>();
		_declarativeLifetimeChildren.RemoveWhere(owner => owner.IsDisposed);
		_declarativeLifetimeChildren.Add(child);
	}

	internal void ReleaseDeclarativeLifetimeOwner()
	{
		var owner = _declarativeLifetimeOwner;
		_declarativeLifetimeOwner = null;
		owner?._declarativeLifetimeChildren?.Remove(this);
	}

	internal void DisposeDeclarativeLifetimeChildren()
	{
		if (_declarativeLifetimeChildren is null)
			return;

		var children = new List<View>(_declarativeLifetimeChildren);
		_declarativeLifetimeChildren.Clear();
		_declarativeLifetimeChildren = null;
		foreach (var child in children)
		{
			if (!ReferenceEquals(
				child._declarativeLifetimeOwner,
				this))
				continue;

			child._declarativeLifetimeOwner = null;
			child.Dispose();
		}
	}

	internal readonly struct DeclarativeRenderScope
	{
		readonly View _owner;
		readonly DeclarativeViewIdentity _ownerIdentity;
		readonly long _invocation;
		readonly long _constructionStart;

		internal DeclarativeRenderScope(
			View owner,
			DeclarativeViewIdentity ownerIdentity,
			long invocation,
			long constructionStart)
		{
			_owner = owner;
			_ownerIdentity = ownerIdentity;
			_invocation = invocation;
			_constructionStart = constructionStart;
		}

		internal void Bind(View root)
		{
			if (root is null)
				return;

			var constructionEnd = System.Threading.Volatile.Read(
				ref _nextConstructionOrder);
			Bind(root, "0", constructionEnd);
		}

		void Bind(View view, string position, long constructionEnd)
		{
			if (view is NavigationView navigation)
				_owner.TrackDeclarativeLifetimeChild(navigation);

			if (view._constructionOrder > _constructionStart &&
				view._constructionOrder <= constructionEnd)
			{
				view._declarativeDeclaration = new DeclarativeDeclaration(
					_owner,
					_ownerIdentity.Append(position),
					_invocation);
			}

			view.CheckForBody();
			if (view.Body is not null)
			{
				_owner.TrackDeclarativeLifetimeChild(view);
				return;
			}
			if (view is not IContainerView container)
				return;

			var children = container.GetChildren();
			for (var index = 0; index < children.Count; index++)
			{
				if (children[index] is { } child)
					Bind(child, $"{position}.{index}", constructionEnd);
			}
		}
	}

	sealed class DeclarativeDeclaration
	{
		public DeclarativeDeclaration(
			View owner,
			DeclarativeViewIdentity identity,
			long invocation)
		{
			Owner = owner;
			Identity = identity;
			Invocation = invocation;
		}

		public View Owner { get; }
		public DeclarativeViewIdentity Identity { get; }
		public long Invocation { get; }
	}

	internal readonly struct DeclarativeViewIdentity : IEquatable<DeclarativeViewIdentity>
	{
		public DeclarativeViewIdentity(object root, string position)
		{
			Root = root;
			Position = position;
		}

		object Root { get; }
		string Position { get; }

		public DeclarativeViewIdentity Append(string position)
			=> new(
				Root,
				string.IsNullOrEmpty(Position)
					? position
					: $"{Position}/{position}");

		public DeclarativeViewIdentity EnterBody(Type ownerType)
			=> Append($"body:{ownerType.AssemblyQualifiedName}");

		public bool Equals(DeclarativeViewIdentity other)
			=> ReferenceEquals(Root, other.Root) &&
				string.Equals(Position, other.Position, StringComparison.Ordinal);

		public override bool Equals(object? obj)
			=> obj is DeclarativeViewIdentity other && Equals(other);

		public override int GetHashCode()
			=> HashCode.Combine(
				System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Root),
				Position);
	}

	internal virtual void AdoptRetainedIdentityStateFrom(View replacement)
	{
		if (replacement is null)
			throw new ArgumentNullException(nameof(replacement));
		if (replacement.GetType() != GetType())
			throw new InvalidOperationException("Retained identity state can only move between identical view types.");

		var stableKey = this.GetKey();

		_context?.Clear();
		_localContext?.Clear();
		_context = replacement._context;
		_localContext = replacement._localContext;
		replacement._context = null;
		replacement._localContext = null;

		AccessibilityId = replacement.AccessibilityId;
		TransferPropertySubscriptionsFrom(replacement);
		TransferDeclarativeMembersFrom(replacement);

		if (!string.IsNullOrEmpty(stableKey))
			LocalContext(true).dictionary[EnvironmentKeys.View.Key] = stableKey;

		InvalidateMeasurement();
	}

	internal void AdoptReconciledStructureFrom(View replacement)
	{
		if (replacement.builtView is not null || builtView is not null)
		{
			var outgoing = builtView;
			builtView = replacement.builtView;
			replacement.builtView = null;
			if (builtView is not null)
				builtView.Parent = this;
			if (outgoing is not null && !ReferenceEquals(outgoing, builtView))
				outgoing.Dispose();
			return;
		}

		if (this is ContentView retainedContent &&
			replacement is ContentView replacementContent)
		{
			var outgoing = retainedContent.Content;
			var incoming = replacementContent.Content;
			replacementContent.Content = null;
			retainedContent.Content = null;
			if (incoming is not null)
				retainedContent.Add(incoming);
			if (outgoing is not null && !ReferenceEquals(outgoing, incoming))
				outgoing.Dispose();
			return;
		}

		if (this is not IList<View> retainedChildren ||
			replacement is not IList<View> replacementChildren)
			return;

		var outgoingChildren = new List<View>(retainedChildren);
		var incomingChildren = new List<View>(replacementChildren);
		replacementChildren.Clear();
		retainedChildren.Clear();
		foreach (var child in incomingChildren)
			retainedChildren.Add(child);

		foreach (var outgoing in outgoingChildren)
		{
			if (outgoing is null ||
				incomingChildren.Exists(incoming => ReferenceEquals(incoming, outgoing)))
				continue;
			outgoing.Dispose();
		}
	}

	void TransferPropertySubscriptionsFrom(View replacement)
	{
		if (_propertySubscriptions is not null)
		{
			foreach (var subscription in _propertySubscriptions)
				subscription.Dispose();
		}

		_propertySubscriptions = replacement._propertySubscriptions;
		replacement._propertySubscriptions = null;

		if (_propertySubscriptions is null)
			return;

		foreach (var subscription in _propertySubscriptions)
			if (subscription is IViewBoundPropertySubscription bound)
				bound.RebindToView(this);
	}

	void TransferDeclarativeMembersFrom(View replacement)
	{
		for (var type = GetType();
			type is not null && type != typeof(View);
			type = type.BaseType)
		{
			foreach (var field in type.GetFields(
				BindingFlags.Instance | BindingFlags.Public |
				BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
			{
				if (field.IsStatic || !IsDeclarativeMember(type, field))
					continue;

				var incoming = field.GetValue(replacement);
				var outgoing = field.GetValue(this);
				if (ReferenceEquals(incoming, outgoing))
				{
					if (incoming is IViewBoundPropertySubscription)
						field.SetValue(replacement, null);
					continue;
				}

				if (outgoing is IDisposable disposable)
					disposable.Dispose();

				field.SetValue(this, incoming);
				field.SetValue(replacement, null);

				if (incoming is IViewBoundPropertySubscription bound)
					bound.RebindToView(this);
			}
		}
	}

	internal void DisposeDeclarativeMemberSubscriptions()
	{
		for (var type = GetType();
			type is not null && type != typeof(View);
			type = type.BaseType)
		{
			foreach (var field in type.GetFields(
				BindingFlags.Instance | BindingFlags.Public |
					BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
			{
				if (field.IsStatic ||
					!typeof(IViewBoundPropertySubscription).IsAssignableFrom(field.FieldType))
					continue;

				if (field.GetValue(this) is IDisposable disposable)
					disposable.Dispose();
				if (!field.IsInitOnly)
					field.SetValue(this, null);
			}
		}
	}

	[UnconditionalSuppressMessage(
		"Trimming",
		"IL2070",
		Justification = "View carries the matching property preservation contract; the linker does not propagate it through Type.BaseType traversal.")]
	static bool IsDeclarativeMember(Type declaringType, FieldInfo field)
	{
		var fieldType = field.FieldType;
		if (field.IsInitOnly ||
			(!typeof(Delegate).IsAssignableFrom(fieldType) &&
			 !typeof(IViewBoundPropertySubscription).IsAssignableFrom(fieldType)))
			return false;

		var propertyName = field.Name;
		if (propertyName.StartsWith("<", StringComparison.Ordinal) &&
			propertyName.EndsWith(">k__BackingField", StringComparison.Ordinal))
		{
			propertyName = propertyName[1..propertyName.IndexOf('>')];
		}
		else if (propertyName.Length > 0)
		{
			propertyName = propertyName.TrimStart('_');
			if (propertyName.Length == 0)
				return false;
			propertyName = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
		}

		for (var type = declaringType; type is not null && type != typeof(View); type = type.BaseType)
		{
			var property = type.GetProperty(
				propertyName,
				BindingFlags.Instance | BindingFlags.Public |
					BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
			if (property?.PropertyType == fieldType)
				return true;
		}

		return false;
	}
}
