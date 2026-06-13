#nullable enable
#if IOS
using Comet.Backend;
using Comet.Platform.SwiftUI;

namespace Comet
{
	// CreateBackendNode overrides mapping wave-1 controls to their SwiftUI node kind.
	// The only references to SwiftUINode, so unused controls trim with their nodes.

	public partial class Text
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("text");
	}

	public partial class Button
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("button");
	}

	public partial class VStack
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("vstack");
	}

	public partial class HStack
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("hstack");
	}

	public partial class ZStack
	{
		protected internal override ICometBackendNode CreateBackendNode(BackendContext context)
			=> new SwiftUINode("zstack");
	}
}
#endif
