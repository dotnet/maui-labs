using ObjCRuntime;

namespace Microsoft.Maui.Essentials.AI;

[Native]
internal enum ChatClientError : long
{
	EmptyMessages = 1,
	InvalidRole = 2,
	InvalidContent = 3,
	Cancelled = 4
}

[Native]
internal enum ChatRoleNative : long
{
	User = 1,
	Assistant = 2,
	System = 3,
	Tool = 4,
}

[Native]
internal enum ResponseUpdateTypeNative : long
{
	Content = 0,
	ToolCall = 1,
	ToolResult = 2
}

[Native]
internal enum VisionDocumentClientErrorNative : long
{
	Cancelled = 1,
	InvalidRevision = 2,
	InvalidRegionOfInterest = 3,
	UnsupportedBarcodeSymbology = 4
}

[Native]
internal enum VisionDocumentNodeKindNative : long
{
	Title = 0,
	Paragraph = 1,
	Table = 2,
	TableCell = 3,
	List = 4,
	ListItem = 5,
	Barcode = 6
}
