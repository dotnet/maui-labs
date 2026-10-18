using System.Text;
using System.Text.Json;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Converts complete JSON objects (from an AI model that post-processes its output) back into 
/// streaming chunks. The AI model receives progressive JSON internally but outputs complete 
/// valid JSON objects each time, sometimes with reordered properties.
/// </summary>
/// <remarks>
/// <para>
/// <b>Problem:</b> AI models may output complete JSON objects at each step, but we want to 
/// stream partial output to the user for better UX.
/// </para>
/// <para>
/// <b>Solution:</b> This chunker compares successive complete JSON snapshots and emits only 
/// the delta (new/changed content) as streaming chunks.
/// </para>
/// <para>
/// <b>Key Design Principles:</b>
/// <list type="bullet">
///   <item>Growable types (strings, arrays, objects) are potentially partial until proven complete</item>
///   <item>Every value may be provisional; scalars are deferred until completion evidence or Flush</item>
///   <item>Duplicate snapshots are not completion signals for open values</item>
///   <item>Multiple new growables at same level → add to pending, wait for next chunk to disambiguate</item>
///   <item>Use path-based tracking (e.g., "days[0].title") not position-based</item>
///   <item>Emitted strings only grow; revisions to completed output throw NotSupportedException</item>
/// </list>
/// </para>
/// <para>
/// See json-stream-chunker-design.md for full algorithm documentation.
/// </para>
/// </remarks>
internal sealed class JsonStreamChunker : StreamChunkerBase
{
	// ═══════════════════════════════════════════════════════════════════════════════════════════
	// STATE TRACKING
	// ═══════════════════════════════════════════════════════════════════════════════════════════

	/// <summary>Flattened path→value dictionary from the previous chunk.</summary>
	private Dictionary<string, JsonValue>? _prevState;

	/// <summary>Path of the currently open string (no closing quote emitted yet).</summary>
	private string? _openStringPath;

	/// <summary>An array scalar whose position is emitted but whose provisional value is deferred.</summary>
	private (string Path, JsonElement Value)? _openScalar;

	/// <summary>Provisional object properties, deferred without blocking partial string siblings.</summary>
	private readonly Dictionary<string, JsonElement> _pendingScalars = new();

	/// <summary>Completed primitives, retained to reject revisions that cannot be streamed.</summary>
	private readonly Dictionary<string, JsonValue> _emittedPrimitives = new();

	/// <summary>Container shapes already committed to the append-only output.</summary>
	private readonly Dictionary<string, JsonValueKind> _emittedContainers = new();

	/// <summary>Tracks emitted string values by path for extension detection.</summary>
	private readonly Dictionary<string, string> _emittedStrings = new();

	/// <summary>Strings waiting for next chunk to determine which is the active/partial one.</summary>
	private readonly Dictionary<string, string> _pendingStrings = new();

	/// <summary>Containers (arrays/objects) waiting for next chunk to see if they grow.</summary>
	private readonly Dictionary<string, JsonElement> _pendingContainers = new();

	/// <summary>All paths we've already output (for comma management).</summary>
	private readonly HashSet<string> _emittedPaths = new();

	/// <summary>Stack of open containers (objects/arrays) for proper closing.</summary>
	private readonly Stack<(string Path, bool IsArray)> _openStructures = new();

	/// <summary>Represents a JSON value with its kind and content.</summary>
	private record struct JsonValue(JsonValueKind Kind, string? StringValue, string? RawValue);

	// ═══════════════════════════════════════════════════════════════════════════════════════════
	// HELPER: Is this a "growable" value type?
	// ═══════════════════════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// Returns true for kinds whose JSON syntax can stream incrementally.
	/// Other kinds may change too, but their provisional tokens must be deferred.
	/// </summary>
	private static bool IsGrowable(JsonValueKind kind) =>
		kind == JsonValueKind.String || kind == JsonValueKind.Array || kind == JsonValueKind.Object;

	private void SetPendingContainer(string path, JsonElement value) =>
		_pendingContainers[path] = value.Clone();

	private void RefreshPendingValues(JsonElement root)
	{
		foreach (var path in _pendingContainers.Keys.Concat(_pendingStrings.Keys).Concat(_pendingScalars.Keys).ToArray())
		{
			var current = GetElementAtPath(root, path);
			if (current is not { } value)
			{
				_pendingContainers.Remove(path);
				_pendingStrings.Remove(path);
				_pendingScalars.Remove(path);
				continue;
			}

			if (value.ValueKind == JsonValueKind.String)
			{
				_pendingStrings[path] = value.GetString() ?? "";
				_pendingContainers.Remove(path);
				_pendingScalars.Remove(path);
			}
			else if (value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
			{
				SetPendingContainer(path, value);
				_pendingStrings.Remove(path);
				_pendingScalars.Remove(path);
			}
			else
			{
				_pendingScalars[path] = value.Clone();
				_pendingContainers.Remove(path);
				_pendingStrings.Remove(path);
			}
		}
	}

	/// <summary>
	/// Completes the currently open scalar before output moves to another value.
	/// </summary>
	private void CloseOpenValue(StringBuilder sb)
	{
		if (_openStringPath != null)
		{
			sb.Append('"');
			_openStringPath = null;
		}

		if (_openScalar is { } scalar)
		{
			sb.Append(scalar.Value.GetRawText());
			TrackPrimitive(scalar.Path, scalar.Value);
			_openScalar = null;
		}
	}

	/// <summary>
	/// Emits and removes any pending items that are children of the given path.
	/// Used when we know a subtree is complete (e.g., moving to next array item in first chunk).
	/// </summary>
	private void EmitPendingItemsUnder(StringBuilder sb, string parentPath)
	{
		foreach (var path in _pendingScalars.Keys
			.Where(path => path.StartsWith(parentPath + ".", StringComparison.Ordinal) ||
				path.StartsWith(parentPath + "[", StringComparison.Ordinal)).OrderBy(path => path).ToArray())
		{
			if (_pendingScalars.ContainsKey(path))
				EmitPendingScalar(sb, path);
		}

		// Find pending strings under this path
		var stringsToEmit = _pendingStrings
			.Where(kvp => kvp.Key.StartsWith(parentPath + ".", StringComparison.Ordinal) ||
						  kvp.Key.StartsWith(parentPath + "[", StringComparison.Ordinal))
			.OrderBy(kvp => kvp.Key)
			.ToList();

		foreach (var (path, value) in stringsToEmit)
		{
			if (!_pendingStrings.ContainsKey(path))
				continue;

			EmitPendingString(sb, path, value, keepOpen: false);
			_pendingStrings.Remove(path);
		}

		// Find pending containers under this path
		var containersToEmit = _pendingContainers
			.Where(kvp => kvp.Key.StartsWith(parentPath + ".", StringComparison.Ordinal) ||
						  kvp.Key.StartsWith(parentPath + "[", StringComparison.Ordinal))
			.OrderBy(kvp => kvp.Key)
			.ToList();

		foreach (var (path, _) in containersToEmit)
		{
			if (!_pendingContainers.ContainsKey(path))
				continue;

			EmitPendingContainer(sb, path, complete: true);
		}
	}

	// ═══════════════════════════════════════════════════════════════════════════════════════════
	// PUBLIC API
	// ═══════════════════════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// Processes a complete JSON snapshot and returns a streaming chunk representing the delta.
	/// </summary>
	/// <param name="completeJson">A complete, valid JSON object representing the current state.</param>
	/// <returns>A string chunk to emit (may be empty if no output yet). Concatenating all chunks yields valid JSON.</returns>
	/// <remarks>
	/// Call this method for each complete JSON object received from the AI model.
	/// The chunker maintains state between calls to track what has been emitted.
	/// </remarks>
	/// <exception cref="JsonException">The snapshot is not complete, valid JSON.</exception>
	/// <exception cref="NotSupportedException">The root is not an object, or a snapshot revises committed output.</exception>
	public override string Process(string completeJson)
	{
		using (var doc = JsonDocument.Parse(completeJson))
		{
			if (doc.RootElement.ValueKind != JsonValueKind.Object)
				throw new NotSupportedException("JSON snapshots must have an object root.");

			ValidateSnapshot(doc.RootElement);
			RefreshPendingValues(doc.RootElement);
			var currState = FlattenJson(doc.RootElement, "");
			if (_openScalar is { } scalar)
				_openScalar = (scalar.Path, GetElementAtPath(doc.RootElement, scalar.Path)!.Value.Clone());

			string result;
			if (_prevState == null)
			{
				// First chunk - emit structure with strings potentially open
				result = ProcessFirstChunk(currState, doc.RootElement);
			}
			else
			{
				// Subsequent chunk - compare and emit deltas
				result = ProcessSubsequentChunk(currState, doc.RootElement);
			}

			_prevState = currState;
			return result;
		}
	}

	/// <summary>
	/// Flushes any remaining state and closes all open structures.
	/// </summary>
	/// <returns>Final chunk to complete the JSON output (may be empty).</returns>
	/// <remarks>
	/// Must be called after all JSON snapshots have been processed to properly close
	/// any pending strings and open containers (objects/arrays).
	/// </remarks>
	public override string Flush()
	{
		var sb = new StringBuilder();
		foreach (var path in _pendingScalars.Keys.OrderBy(path => path).ToArray())
		{
			if (_pendingScalars.ContainsKey(path))
				EmitPendingScalar(sb, path);
		}

		// Emit any pending strings that never got disambiguated
		if (_pendingStrings.Count > 0)
		{
			foreach (var (path, value) in _pendingStrings.OrderBy(p => p.Key).ToList())
			{
				if (_pendingStrings.ContainsKey(path))
					EmitPendingString(sb, path, value, keepOpen: false);
			}
			_pendingStrings.Clear();
		}

		// Emit any pending containers that never got disambiguated
		if (_pendingContainers.Count > 0)
		{
			foreach (var path in _pendingContainers.Keys.OrderBy(path => path).ToArray())
			{
				if (!_pendingContainers.ContainsKey(path))
					continue;

				EmitPendingContainer(sb, path, complete: true);
			}
			_pendingContainers.Clear();
		}

		// Complete any open scalar
		CloseOpenValue(sb);

		// Close all open structures (objects/arrays)
		while (_openStructures.Count > 0)
		{
			var (_, isArray) = _openStructures.Pop();
			sb.Append(isArray ? ']' : '}');
		}

		return sb.ToString();
	}

	/// <inheritdoc />
	public override void Reset()
	{
		// Setting _prevState to null causes the next Process() call to treat the
		// incoming JSON as a "first chunk" (see the _prevState == null check in
		// Process), which is the intended behavior after a reset — the chunker
		// should emit the full initial structure for the new stream rather than
		// computing a delta against stale state from before the reset.
		_prevState = null;
		_openStringPath = null;
		_openScalar = null;
		_pendingScalars.Clear();
		_emittedPrimitives.Clear();
		_emittedContainers.Clear();
		_emittedStrings.Clear();
		_pendingStrings.Clear();
		_pendingContainers.Clear();
		_emittedPaths.Clear();
		_openStructures.Clear();
	}

	// ═══════════════════════════════════════════════════════════════════════════════════════════
	// FIRST CHUNK PROCESSING
	// ═══════════════════════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// Processes the first JSON chunk - emits initial structure with strings potentially open.
	/// </summary>
	private string ProcessFirstChunk(Dictionary<string, JsonValue> state, JsonElement elem)
	{
		var sb = new StringBuilder();
		sb.Append('{');
		_openStructures.Push(("", false));
		_emittedPaths.Add("");

		// Group strings by parent to determine if we have multiple at same level (ambiguous)
		var stringsByParent = GroupStringsByParent(state);
		EmitStructure(sb, elem, "", state, stringsByParent);

		return sb.ToString();
	}

	// ═══════════════════════════════════════════════════════════════════════════════════════════
	// SUBSEQUENT CHUNK PROCESSING
	// ═══════════════════════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// Append-only output cannot retract completed values or rewrite an emitted string prefix.
	/// Reject such snapshots rather than silently producing JSON that disagrees with the model.
	/// </summary>
	private void ValidateSnapshot(JsonElement root)
	{
		if (_prevState == null)
			return;

		foreach (var (path, kind) in _emittedContainers)
		{
			var current = GetElementAtPath(root, path);
			if (current == null || current.Value.ValueKind != kind)
				throw new NotSupportedException($"JSON snapshot removed or changed the emitted container at '{path}'.");
		}

		foreach (var (path, emitted) in _emittedStrings)
		{
			var element = GetElementAtPath(root, path);
			if (element == null || element.Value.ValueKind != JsonValueKind.String)
				throw new NotSupportedException($"JSON snapshot removed or changed the emitted string at '{path}'.");
			var current = element.Value.GetString() ?? "";
			if (path == _openStringPath
				? !current.StartsWith(emitted, StringComparison.Ordinal)
				: current != emitted)
				throw new NotSupportedException($"JSON snapshot revised the emitted string at '{path}'.");
		}

		foreach (var (path, emitted) in _emittedPrimitives)
		{
			var current = GetElementAtPath(root, path);
			if (current == null || current.Value.ValueKind != emitted.Kind ||
				(emitted.Kind == JsonValueKind.Number && current.Value.GetRawText() != emitted.RawValue))
				throw new NotSupportedException($"JSON snapshot revised the completed value at '{path}'.");
		}

		if (_openScalar is { } scalar && GetElementAtPath(root, scalar.Path) == null)
			throw new NotSupportedException($"JSON snapshot removed the active array item at '{scalar.Path}'.");
	}

	/// <summary>
	/// Processes subsequent chunks - compares with previous state and emits deltas.
	/// </summary>
	private string ProcessSubsequentChunk(Dictionary<string, JsonValue> currState, JsonElement currElem)
	{
		var sb = new StringBuilder();

		if (_openScalar is { } scalar && IsGrowable(scalar.Value.ValueKind))
		{
			_openScalar = null;
			EmitValue(sb, scalar.Value, scalar.Path);
		}

		// Step 1: Handle any currently open string (check if it changed, has new sibling, or parent changed)
		if (_openStringPath != null)
			HandleOpenString(sb, currState);

		var pendingPaths = _pendingStrings.Keys.Concat(_pendingContainers.Keys).ToHashSet();
		// Extend open subtrees before pending siblings can close their ancestors.
		ProcessNewContent(sb, _prevState!, currState, currElem, "");

		if (_openStringPath == null && (_pendingStrings.Count > 0 || _pendingContainers.Count > 0))
			ResolvePendingItems(sb, currState, pendingPaths);

		return sb.ToString();
	}

	/// <summary>
	/// Handles an open string from the previous chunk.
	/// Determines if string is complete (has new sibling or parent changed) or still growing.
	/// </summary>
	/// <remarks>
	/// String completion rules:<br/>
	/// 1. New sibling at same level → string is complete (close it)<br/>
	/// 2. Parent-level change (e.g., new array item) → string is complete (close it)<br/>
	/// 3. Value changed but no sibling/parent change → emit extension, keep open<br/>
	/// 4. Value unchanged and another pending growable advances → move the streaming frontier<br/>
	/// 5. Otherwise keep open; duplicate snapshots do not prove completion
	/// </remarks>
	private void HandleOpenString(StringBuilder sb, Dictionary<string, JsonValue> currState)
	{
		if (_openStringPath == null)
			return;

		var emitted = _emittedStrings.GetValueOrDefault(_openStringPath, "");
		var curr = currState.TryGetValue(_openStringPath, out var currVal) && currVal.Kind == JsonValueKind.String
			? currVal.StringValue ?? ""
			: "";

		var parentPath = GetParentPath(_openStringPath);
		bool hasNewSibling = HasNewSiblingAt(parentPath, _prevState!, currState);
		bool hasParentChange = HasParentLevelChange(_openStringPath, _prevState!, currState);
		bool frontierMoved = curr == emitted &&
			(_pendingStrings.Keys.Any(path => StringChanged(path, _prevState!, currState)) ||
			 _pendingContainers.Keys.Any(path => HasStreamableGrowth(path, _prevState!, currState)));

		if (hasNewSibling || hasParentChange || frontierMoved)
		{
			// Structural progression or another growing value moves the streaming frontier.
			if (curr != emitted && curr.Length > emitted.Length)
				sb.Append(Escape(curr.Substring(emitted.Length)));
			_emittedStrings[_openStringPath] = curr;
			sb.Append('"');
			_openStringPath = null;
		}
		else if (curr != emitted)
		{
			// String changed but no completion signal - emit extension, keep open
			if (curr.Length > emitted.Length)
				sb.Append(Escape(curr.Substring(emitted.Length)));
			_emittedStrings[_openStringPath] = curr;
		}
	}

	/// <summary>
	/// Resolves pending items (strings and containers) from previous chunk by comparing current values.
	/// </summary>
	/// <remarks>
	/// When we had multiple new growables at the same level, we couldn't tell which was partial.
	/// Unchanged items remain deferred because they may resume growing in a later snapshot.
	/// </remarks>
	private void ResolvePendingItems(StringBuilder sb, Dictionary<string, JsonValue> currState, HashSet<string> pendingPaths)
	{
		// Determine which pending item changed
		var changedStrings = new List<(string Path, string Value)>();
		var changedContainers = new List<string>();
		var completeStrings = new List<(string Path, string Value)>();
		var completeContainers = new List<string>();

		// Check pending strings
		foreach (var path in _pendingStrings.Keys)
		{
			if (!pendingPaths.Contains(path))
				continue;
			if (!currState.TryGetValue(path, out var currVal) || currVal.Kind != JsonValueKind.String)
				continue;

			var currValue = currVal.StringValue ?? "";
			var parentPath = GetParentPath(path);
			if (HasNewSiblingAt(parentPath, _prevState!, currState) ||
				HasParentLevelChange(path, _prevState!, currState))
			{
				completeStrings.Add((path, currValue));
			}
			else if (StringChanged(path, _prevState!, currState))
				changedStrings.Add((path, currValue));
		}

		// Check pending containers
		foreach (var path in _pendingContainers.Keys)
		{
			if (!pendingPaths.Contains(path))
				continue;
			var parentPath = GetParentPath(path);
			if (HasNewSiblingAt(parentPath, _prevState!, currState) ||
				HasParentLevelChange(path, _prevState!, currState))
			{
				completeContainers.Add(path);
			}
			else if (HasContainerGrown(path, _prevState!, currState))
				changedContainers.Add(path);
		}

		foreach (var (path, value) in changedStrings.Concat(completeStrings))
			_pendingStrings[path] = value;

		var allComplete = completeStrings.Select(item =>
				(item.Path, IsString: true, Value: item.Value))
			.Concat(completeContainers.Select(path =>
				(Path: path, IsString: false, Value: "")))
			.OrderBy(item => item.Path)
			.ToList();

		foreach (var item in allComplete)
		{
			if (item.IsString)
			{
				if (_pendingStrings.ContainsKey(item.Path))
					EmitPendingString(sb, item.Path, item.Value, keepOpen: false);
			}
			else
			{
				if (_pendingContainers.ContainsKey(item.Path))
					EmitPendingContainer(sb, item.Path, complete: true);
			}
		}

		// A single changed growable is the active value and can stream. When several
		// grow together, keep all of them pending until a later snapshot disambiguates
		// which one is still active.
		var changedCount = changedStrings.Count + changedContainers.Count;
		if (changedCount == 1 && changedStrings.Count == 1)
		{
			var (changedStringPath, changedStringValue) = changedStrings[0];
			if (_pendingStrings.ContainsKey(changedStringPath))
			{
				EmitPendingString(sb, changedStringPath, changedStringValue, keepOpen: true);

				// Check if it should be closed due to sibling
				if (HasNewSiblingAt(GetParentPath(changedStringPath), _prevState!, currState))
				{
					sb.Append('"');
					_openStringPath = null;
				}
			}
		}
		else if (changedCount == 1)
		{
			var changedContainerPath = changedContainers[0];
			if (_pendingContainers.ContainsKey(changedContainerPath))
				EmitPendingContainer(sb, changedContainerPath, complete: false);
		}

		// Remove only the items that were pending when we entered this method.
		// Don't clear everything - EmitPendingContainer may have added NEW pending items
		// when processing nested content.
		foreach (var (path, _) in completeStrings)
			_pendingStrings.Remove(path);
		if (changedCount == 1 && changedStrings.Count == 1)
			_pendingStrings.Remove(changedStrings[0].Path);

		foreach (var path in completeContainers)
			_pendingContainers.Remove(path);
		if (changedCount == 1 && changedContainers.Count == 1)
			_pendingContainers.Remove(changedContainers[0]);
	}

	/// <summary>
	/// Checks if a container (array or object) has grown since previous state.
	/// </summary>
	private bool HasContainerGrown(string path, Dictionary<string, JsonValue> prevState, Dictionary<string, JsonValue> currState)
	{
		if (prevState.TryGetValue(path, out var previous) && currState.TryGetValue(path, out var current) &&
			previous.Kind != current.Kind)
			return true;

		// Check if any children were added or changed
		var prevChildren = prevState.Keys.Where(k => k.StartsWith(path + ".") || k.StartsWith(path + "[")).ToHashSet();
		var currChildren = currState.Keys.Where(k => k.StartsWith(path + ".") || k.StartsWith(path + "[")).ToHashSet();

		if (!currChildren.SetEquals(prevChildren))
			return true;

		// A growable child can change without adding a path, such as an array's last
		// string extending from "su" to "summer".
		foreach (var key in currChildren)
		{
			if (!prevState.TryGetValue(key, out var previousValue) ||
				currState[key] != previousValue)
			{
				return true;
			}
		}

		return false;
	}

	private static bool StringChanged(string path, Dictionary<string, JsonValue> previous, Dictionary<string, JsonValue> current) =>
		current.TryGetValue(path, out var value) && value.Kind == JsonValueKind.String &&
		(!previous.TryGetValue(path, out var old) || old.Kind != JsonValueKind.String || old.StringValue != value.StringValue);

	private static bool HasStreamableGrowth(string path, Dictionary<string, JsonValue> previous, Dictionary<string, JsonValue> current) =>
		current.Any(pair => IsDescendant(pair.Key, path) &&
			(!previous.ContainsKey(pair.Key) || StringChanged(pair.Key, previous, current)));

	/// <summary>
	/// Emits a pending container (array or object) that has been resolved.
	/// </summary>
	private void EmitPendingContainer(StringBuilder sb, string path, bool complete)
	{
		var value = _pendingContainers[path];
		_pendingContainers.Remove(path);
		var (parentPath, propName) = SplitPath(path);
		CloseOpenValue(sb);
		CloseStructuresDownTo(sb, parentPath);

		if (HasEmittedSiblingAt(parentPath))
			sb.Append(',');

		sb.Append("\"" + Escape(propName) + "\":");
		_emittedPaths.Add(path);

		if (complete)
			EmitCompleteValue(sb, value, path);
		else
			EmitValue(sb, value, path);
	}

	/// <summary>
	/// Emits complete array content (all closed).
	/// </summary>
	private void EmitCompleteArrayContent(StringBuilder sb, JsonElement elem, string path)
	{
		int idx = 0;
		foreach (var item in elem.EnumerateArray())
		{
			if (idx > 0)
			{
				CloseOpenValue(sb);
				CloseStructuresDownTo(sb, path);
				sb.Append(',');
			}

			var itemPath = path + "[" + idx + "]";
			_emittedPaths.Add(itemPath);
			EmitCompleteValue(sb, item, itemPath);
			idx++;
		}
	}

	/// <summary>
	/// Emits complete object content (all closed).
	/// </summary>
	private void EmitCompleteObjectContent(StringBuilder sb, JsonElement elem, string path)
	{
		bool isFirst = true;
		foreach (var prop in elem.EnumerateObject())
		{
			if (!isFirst)
				sb.Append(',');
			isFirst = false;

			var propPath = CombinePath(path, prop.Name);
			sb.Append("\"" + Escape(prop.Name) + "\":");
			_emittedPaths.Add(propPath);
			EmitCompleteValue(sb, prop.Value, propPath);
		}
	}

	/// <summary>
	/// Emits a complete (closed) JSON value at the given path.
	/// Used for values that are fully resolved and should not be left open.
	/// </summary>
	private void EmitCompleteValue(StringBuilder sb, JsonElement elem, string path)
	{
		if (elem.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
			_emittedContainers[path] = elem.ValueKind;
		switch (elem.ValueKind)
		{
			case JsonValueKind.Object:
				sb.Append('{');
				EmitCompleteObjectContent(sb, elem, path);
				sb.Append('}');
				break;
			case JsonValueKind.Array:
				sb.Append('[');
				EmitCompleteArrayContent(sb, elem, path);
				sb.Append(']');
				break;
			case JsonValueKind.String:
				sb.Append('"');
				sb.Append(Escape(elem.GetString() ?? ""));
				sb.Append('"');
				_emittedStrings[path] = elem.GetString() ?? "";
				break;
			default:
				sb.Append(elem.GetRawText());
				TrackPrimitive(path, elem);
				break;
		}
	}

	/// <summary>
	/// Gets a JSON element at the specified path.
	/// </summary>
	private static JsonElement? GetElementAtPath(JsonElement root, string path)
	{
		if (string.IsNullOrEmpty(path))
			return root;

		var current = root;
		var segments = ParsePath(path);

		foreach (var segment in segments)
		{
			if (segment.StartsWith("[") && segment.EndsWith("]"))
			{
				// Array index
				var idxStr = segment.Substring(1, segment.Length - 2);
				if (int.TryParse(idxStr, out var idx) && current.ValueKind == JsonValueKind.Array && idx < current.GetArrayLength())
					current = current[idx];
				else
					return null;
			}
			else
			{
				// Property name
				if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(DecodePathSegment(segment), out var prop))
					current = prop;
				else
					return null;
			}
		}

		return current;
	}

	/// <summary>
	/// Parses a path into segments.
	/// </summary>
	private static List<string> ParsePath(string path)
	{
		var segments = new List<string>();
		var current = new StringBuilder();

		for (int i = 0; i < path.Length; i++)
		{
			char c = path[i];
			if (c == '.')
			{
				if (current.Length > 0)
				{
					segments.Add(current.ToString());
					current.Clear();
				}
			}
			else if (c == '[')
			{
				if (current.Length > 0)
				{
					segments.Add(current.ToString());
					current.Clear();
				}
				// Find matching ]
				int end = path.IndexOf(']', i);
				if (end > i)
				{
					segments.Add(path.Substring(i, end - i + 1));
					i = end;
				}
			}
			else
			{
				current.Append(c);
			}
		}

		if (current.Length > 0)
			segments.Add(current.ToString());

		return segments;
	}

	/// <summary>
	/// Recursively processes new content by comparing previous and current state.
	/// </summary>
	private void ProcessNewContent(StringBuilder sb, Dictionary<string, JsonValue> prevState,
		Dictionary<string, JsonValue> currState, JsonElement currElem, string path)
	{
		if (currElem.ValueKind == JsonValueKind.Object)
			ProcessObjectChanges(sb, prevState, currState, currElem, path);
		else if (currElem.ValueKind == JsonValueKind.Array)
			ProcessArrayChanges(sb, prevState, currState, currElem, path);
	}

	/// <summary>
	/// Processes changes within an object - identifies new properties and recurses into existing ones.
	/// </summary>
	private void ProcessObjectChanges(StringBuilder sb, Dictionary<string, JsonValue> prevState,
		Dictionary<string, JsonValue> currState, JsonElement currElem, string path)
	{
		var newStringProps = new List<(string Name, JsonElement Value, string Path)>();
		var newContainerProps = new List<(string Name, JsonElement Value, string Path, bool IsArray)>();
		var newNonGrowableProps = new List<(string Name, JsonElement Value, string Path)>();
		int newGrowablesCount = 0;

		foreach (var prop in currElem.EnumerateObject())
		{
			var propPath = CombinePath(path, prop.Name);

			if (_emittedPaths.Contains(propPath) || _pendingStrings.ContainsKey(propPath) ||
				_pendingContainers.ContainsKey(propPath) || _pendingScalars.ContainsKey(propPath))
			{
				// Existing property - recurse into emitted non-strings. Pending containers
				// must remain opaque until their property name and opening structure are emitted.
				if (prop.Value.ValueKind != JsonValueKind.String &&
					!_pendingContainers.ContainsKey(propPath))
					ProcessNewContent(sb, prevState, currState, prop.Value, propPath);
			}
			else
			{
				// New property - categorize it
				switch (prop.Value.ValueKind)
				{
					case JsonValueKind.String:
						newGrowablesCount++;
						newStringProps.Add((prop.Name, prop.Value, propPath));
						break;
					case JsonValueKind.Array:
						newGrowablesCount++;
						newContainerProps.Add((prop.Name, prop.Value, propPath, true));
						break;
					case JsonValueKind.Object:
						newGrowablesCount++;
						newContainerProps.Add((prop.Name, prop.Value, propPath, false));
						break;
					default:
						newNonGrowableProps.Add((prop.Name, prop.Value, propPath));
						break;
				}
			}
		}

		if (newNonGrowableProps.Count > 0 || newGrowablesCount > 0)
			EnsureStructureOpen(path);

		// Defer provisional scalars without blocking growable properties.
		foreach (var (name, value, propPath) in newNonGrowableProps)
			EmitNewProperty(sb, name, value, propPath, path);

		// Handle new growable properties
		if (newGrowablesCount == 1)
		{
			// Only one growable - emit it appropriately
			if (newStringProps.Count == 1 && _openStringPath == null)
			{
				var (name, value, propPath) = newStringProps[0];
				EmitNewStringProperty(sb, name, value.GetString() ?? "", propPath, path, keepOpen: true);
			}
			else if (newStringProps.Count == 1)
			{
				// Already have open string - add to pending
				foreach (var (_, value, propPath) in newStringProps)
					_pendingStrings[propPath] = value.GetString() ?? "";
			}
			else if (newContainerProps.Count == 1)
			{
				var (name, value, propPath, isArray) = newContainerProps[0];
				EmitNewProperty(sb, name, value, propPath, path);
			}
		}
		else if (newGrowablesCount > 1)
		{
			// Multiple growables - add all to pending
			foreach (var (_, value, propPath) in newStringProps)
				_pendingStrings[propPath] = value.GetString() ?? "";
			foreach (var (_, value, propPath, isArray) in newContainerProps)
				SetPendingContainer(propPath, value);
		}
	}

	/// <summary>
	/// Processes changes within an array - identifies new items and recurses into existing ones.
	/// </summary>
	private void ProcessArrayChanges(StringBuilder sb, Dictionary<string, JsonValue> prevState,
		Dictionary<string, JsonValue> currState, JsonElement currElem, string path)
	{
		var items = currElem.EnumerateArray().ToList();

		for (int idx = 0; idx < items.Count; idx++)
		{
			var item = items[idx];
			var itemPath = path + "[" + idx + "]";

			if (_emittedPaths.Contains(itemPath))
			{
				// Existing item - recurse into non-strings
				if (item.ValueKind != JsonValueKind.String)
					ProcessNewContent(sb, prevState, currState, item, itemPath);
			}
			else
			{
				EnsureStructureOpen(path);
				EmitNewArrayItem(sb, item, itemPath, path, needsComma: idx > 0);
			}
		}
	}

	// ═══════════════════════════════════════════════════════════════════════════════════════════
	// VALUE EMISSION (for subsequent chunks)
	// ═══════════════════════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// Emits a string property with the given name, value, and path.
	/// Optionally closes any open string first and keeps the new string open.
	/// </summary>
	private void EmitString(StringBuilder sb, string name, string value, string path, string parentPath, bool closeExisting, bool keepOpen)
	{
		if (closeExisting || _openScalar != null)
			CloseOpenValue(sb);

		CloseStructuresDownTo(sb, parentPath);

		if (HasEmittedSiblingAt(parentPath))
			sb.Append(',');

		sb.Append("\"" + Escape(name) + "\":\"" + Escape(value));

		if (keepOpen)
			_openStringPath = path;
		else
			sb.Append('"');

		_emittedStrings[path] = value;
		_emittedPaths.Add(path);
	}

	/// <summary>
	/// Emits a pending string that has been resolved.
	/// </summary>
	private void EmitPendingString(StringBuilder sb, string path, string value, bool keepOpen)
	{
		_pendingStrings.Remove(path);
		var (parentPath, propName) = SplitPath(path);
		EmitString(sb, propName, value, path, parentPath, closeExisting: true, keepOpen);
	}

	/// <summary>
	/// Emits a new string property (just discovered in current chunk).
	/// </summary>
	private void EmitNewStringProperty(StringBuilder sb, string name, string value, string path, string parentPath, bool keepOpen) =>
		EmitString(sb, name, value, path, parentPath, closeExisting: false, keepOpen);

	/// <summary>
	/// Emits a new non-string property (numbers, bools, null, objects, arrays).
	/// </summary>
	private void EmitNewProperty(StringBuilder sb, string name, JsonElement value, string path, string parentPath)
	{
		if (!IsGrowable(value.ValueKind))
		{
			_pendingScalars[path] = value.Clone();
			return;
		}

		CloseOpenValue(sb);
		CloseStructuresDownTo(sb, parentPath);

		if (HasEmittedSiblingAt(parentPath))
			sb.Append(',');

		sb.Append("\"" + Escape(name) + "\":");
		EmitValue(sb, value, path);
		_emittedPaths.Add(path);
	}

	private void EmitPendingScalar(StringBuilder sb, string path)
	{
		var (parentPath, name) = SplitPath(path);
		CloseOpenValue(sb);
		CloseStructuresDownTo(sb, parentPath);

		if (HasEmittedSiblingAt(parentPath))
			sb.Append(',');

		var value = _pendingScalars[path];
		sb.Append('"').Append(Escape(name)).Append("\":").Append(value.GetRawText());
		TrackPrimitive(path, value);
		_emittedPaths.Add(path);
		_pendingScalars.Remove(path);
	}

	/// <summary>
	/// Emits a new array item.
	/// </summary>
	private void EmitNewArrayItem(StringBuilder sb, JsonElement value, string path, string arrayPath, bool needsComma)
	{
		CloseOpenValue(sb);
		CloseStructuresDownTo(sb, arrayPath);

		if (needsComma)
			sb.Append(',');

		if (value.ValueKind == JsonValueKind.Object)
		{
			sb.Append('{');
			_openStructures.Push((path, false));
			_emittedPaths.Add(path);
			EmitObjectContent(sb, value, path);
		}
		else if (value.ValueKind == JsonValueKind.Array)
		{
			sb.Append('[');
			_openStructures.Push((path, true));
			_emittedPaths.Add(path);
			EmitArrayContent(sb, value, path);
		}
		else
		{
			EmitValue(sb, value, path);
			_emittedPaths.Add(path);
		}
	}

	#region Structure Emission (First Chunk)

	/// <summary>
	/// Recursively emits structure for first chunk processing.
	/// </summary>
	private void EmitStructure(StringBuilder sb, JsonElement elem, string path,
		Dictionary<string, JsonValue> state, Dictionary<string, List<string>> stringsByParent)
	{
		_emittedContainers[path] = elem.ValueKind;
		if (elem.ValueKind == JsonValueKind.Object)
			EmitObjectStructure(sb, elem, path, state, stringsByParent);
		else if (elem.ValueKind == JsonValueKind.Array)
			EmitArrayStructure(sb, elem, path, state, stringsByParent);
	}

	/// <summary>
	/// Emits object structure for first chunk. Handles string ambiguity (single vs multiple growables).
	/// </summary>
	private void EmitObjectStructure(StringBuilder sb, JsonElement elem, string path,
		Dictionary<string, JsonValue> state, Dictionary<string, List<string>> stringsByParent)
	{
		var stringsAtLevel = stringsByParent.GetValueOrDefault(path, new List<string>());
		int growablesAtLevel = CountGrowablesAtLevel(elem, path);

		foreach (var prop in elem.EnumerateObject())
		{
			var propPath = CombinePath(path, prop.Name);

			if (prop.Value.ValueKind == JsonValueKind.String)
			{
				// If only 1 growable at this level AND it's this string, emit it open
				// Otherwise, add to pending (we can't tell which growable will grow)
				if (growablesAtLevel == 1 && stringsAtLevel.Count == 1)
				{
					// Single growable and it's a string - emit it open (potentially partial)
					CloseOpenValue(sb);
					sb.Append("\"" + Escape(prop.Name) + "\":\"" + Escape(prop.Value.GetString() ?? ""));
					_openStringPath = propPath;
					_emittedStrings[propPath] = prop.Value.GetString() ?? "";
					_emittedPaths.Add(propPath);
				}
				else
				{
					// Multiple growables at this level - add string to pending
					_pendingStrings[propPath] = prop.Value.GetString() ?? "";
				}
			}
			else if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
			{
				// Container property (array or object) - may be growable
				bool isArray = prop.Value.ValueKind == JsonValueKind.Array;

				if (growablesAtLevel == 1)
				{
					// Single growable - emit structure
					CloseOpenValue(sb);
					sb.Append("\"" + Escape(prop.Name) + "\":");
					_emittedPaths.Add(propPath);

					sb.Append(isArray ? '[' : '{');
					_openStructures.Push((propPath, isArray));
					EmitStructure(sb, prop.Value, propPath, state, stringsByParent);
				}
				else
				{
					// Multiple growables at this level - add container to pending
					SetPendingContainer(propPath, prop.Value);
				}
			}
			else
			{
				_pendingScalars[propPath] = prop.Value.Clone();
			}
		}
	}

	/// <summary>
	/// Emits array structure for first chunk.
	/// </summary>
	private void EmitArrayStructure(StringBuilder sb, JsonElement elem, string path,
		Dictionary<string, JsonValue> state, Dictionary<string, List<string>> stringsByParent)
	{
		int idx = 0;
		foreach (var item in elem.EnumerateArray())
		{
			var itemPath = path + "[" + idx + "]";

			if (idx > 0)
			{
				// Emit any pending items from the previous array item (they are complete)
				var prevItemPath = path + "[" + (idx - 1) + "]";
				EmitPendingItemsUnder(sb, prevItemPath);

				// Close any open string from previous array item before moving to next
				CloseOpenValue(sb);
				// Close any open structures down to the array level
				CloseStructuresDownTo(sb, path);
				sb.Append(',');
			}
			_emittedPaths.Add(itemPath);

			if (item.ValueKind == JsonValueKind.Object)
			{
				sb.Append('{');
				_openStructures.Push((itemPath, false));
				EmitStructure(sb, item, itemPath, state, stringsByParent);
			}
			else if (item.ValueKind == JsonValueKind.Array)
			{
				sb.Append('[');
				_openStructures.Push((itemPath, true));
				EmitStructure(sb, item, itemPath, state, stringsByParent);
			}
			else if (item.ValueKind == JsonValueKind.String)
			{
				// String in array - emit open (potentially partial)
				sb.Append("\"" + Escape(item.GetString() ?? ""));
				_openStringPath = itemPath;
				_emittedStrings[itemPath] = item.GetString() ?? "";
			}
			else
			{
				EmitValue(sb, item, itemPath);
			}

			idx++;
		}
	}

	/// <summary>
	/// Emits object content (used for new array items that are objects).
	/// </summary>
	private void EmitObjectContent(StringBuilder sb, JsonElement elem, string path)
	{
		_emittedContainers[path] = JsonValueKind.Object;
		var stringProps = new List<(string Name, string Value, string Path)>();
		var containerProps = new List<(string Name, JsonElement Value, string Path, bool IsArray)>();
		int growablesAtLevel = CountGrowablesAtLevel(elem, path);

		foreach (var prop in elem.EnumerateObject())
		{
			var propPath = CombinePath(path, prop.Name);

			if (prop.Value.ValueKind == JsonValueKind.String)
			{
				// Collect strings to handle later
				stringProps.Add((prop.Name, prop.Value.GetString() ?? "", propPath));
			}
			else if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
			{
				// Collect containers to handle later
				containerProps.Add((prop.Name, prop.Value, propPath, prop.Value.ValueKind == JsonValueKind.Array));
			}
			else
			{
				_pendingScalars[propPath] = prop.Value.Clone();
			}
		}

		// Handle growable properties
		if (growablesAtLevel == 1)
		{
			// Single growable at this level
			if (stringProps.Count == 1)
			{
				var (name, value, propPath) = stringProps[0];
				CloseOpenValue(sb);
				sb.Append("\"" + Escape(name) + "\":\"" + Escape(value));
				_openStringPath = propPath;
				_emittedStrings[propPath] = value;
				_emittedPaths.Add(propPath);
			}
			else if (containerProps.Count == 1)
			{
				var (name, value, propPath, isArray) = containerProps[0];
				CloseOpenValue(sb);
				sb.Append("\"" + Escape(name) + "\":");
				_emittedPaths.Add(propPath);

				sb.Append(isArray ? '[' : '{');
				_openStructures.Push((propPath, isArray));
				if (isArray)
					EmitArrayContent(sb, value, propPath);
				else
					EmitObjectContent(sb, value, propPath);
			}
		}
		else if (growablesAtLevel > 1)
		{
			// Multiple growables - add all to pending
			foreach (var (_, value, propPath) in stringProps)
				_pendingStrings[propPath] = value;
			foreach (var (_, value, propPath, isArray) in containerProps)
				SetPendingContainer(propPath, value);
		}
	}

	/// <summary>
	/// Emits array content (used for new array items that are arrays).
	/// </summary>
	private void EmitArrayContent(StringBuilder sb, JsonElement elem, string path)
	{
		_emittedContainers[path] = JsonValueKind.Array;
		int idx = 0;
		foreach (var item in elem.EnumerateArray())
		{
			if (idx > 0)
			{
				CloseOpenValue(sb);
				CloseStructuresDownTo(sb, path);
				sb.Append(',');
			}

			var itemPath = path + "[" + idx + "]";
			_emittedPaths.Add(itemPath);

			if (item.ValueKind == JsonValueKind.Object)
			{
				sb.Append('{');
				_openStructures.Push((itemPath, false));
				EmitObjectContent(sb, item, itemPath);
			}
			else if (item.ValueKind == JsonValueKind.Array)
			{
				sb.Append('[');
				_openStructures.Push((itemPath, true));
				EmitArrayContent(sb, item, itemPath);
			}
			else if (item.ValueKind == JsonValueKind.String)
			{
				sb.Append("\"" + Escape(item.GetString() ?? ""));
				_openStringPath = itemPath;
				_emittedStrings[itemPath] = item.GetString() ?? "";
			}
			else
			{
				EmitValue(sb, item, itemPath);
			}

			idx++;
		}
	}

	#endregion

	#region Value Emission

	/// <summary>
	/// Emits a JSON value (handles all types).
	/// </summary>
	private void EmitValue(StringBuilder sb, JsonElement elem, string path)
	{
		switch (elem.ValueKind)
		{
			case JsonValueKind.Object:
				sb.Append('{');
				_openStructures.Push((path, false));
				EmitObjectContent(sb, elem, path);
				break;
			case JsonValueKind.Array:
				sb.Append('[');
				_openStructures.Push((path, true));
				EmitArrayContent(sb, elem, path);
				break;
			case JsonValueKind.String:
				sb.Append('"');
				sb.Append(Escape(elem.GetString() ?? ""));
				_openStringPath = path;
				_emittedStrings[path] = elem.GetString() ?? "";
				break;
			case JsonValueKind.Number:
			case JsonValueKind.True:
			case JsonValueKind.False:
			case JsonValueKind.Null:
				_openScalar = (path, elem.Clone());
				break;
		}
	}

	#endregion

	#region Structure Management

	private void TrackPrimitive(string path, JsonElement value) =>
		_emittedPrimitives[path] = new JsonValue(value.ValueKind, null,
			value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null);

	private void EnsureStructureOpen(string path)
	{
		if (!_openStructures.Any(structure => structure.Path == path))
			throw new NotSupportedException($"JSON snapshot added content to the completed container at '{path}'.");
	}

	/// <summary>
	/// Closes open structures (objects/arrays) down to the target path.
	/// Used when we need to emit content at a different level in the JSON tree.
	/// </summary>
	private void CloseStructuresDownTo(StringBuilder sb, string targetPath)
	{
		while (_openStructures.Count > 0)
		{
			var (topPath, isArray) = _openStructures.Peek();

			// Check if targetPath is at or inside topPath
			bool isPrefix = targetPath.StartsWith(topPath) &&
				(targetPath.Length == topPath.Length ||
				 targetPath.Length > topPath.Length && (targetPath[topPath.Length] == '.' || targetPath[topPath.Length] == '['));

			if (topPath == "" || isPrefix)
				break;

			if (_pendingStrings.Keys.Any(path => path.StartsWith(topPath + ".", StringComparison.Ordinal) ||
				path.StartsWith(topPath + "[", StringComparison.Ordinal)) ||
				_pendingScalars.Keys.Any(path => path.StartsWith(topPath + ".", StringComparison.Ordinal) ||
				path.StartsWith(topPath + "[", StringComparison.Ordinal)) ||
				_pendingContainers.Keys.Any(path => path.StartsWith(topPath + ".", StringComparison.Ordinal) ||
				path.StartsWith(topPath + "[", StringComparison.Ordinal)))
			{
				EmitPendingItemsUnder(sb, topPath);
				continue;
			}

			_openStructures.Pop();
			sb.Append(isArray ? ']' : '}');
		}
	}

	#endregion

	#region State Comparison Helpers

	/// <summary>
	/// Checks if a new sibling property appeared at the given parent path.
	/// </summary>
	private bool HasNewSiblingAt(string parentPath, Dictionary<string, JsonValue> prevState, Dictionary<string, JsonValue> currState)
	{
		var prevProps = GetPropertiesAtPath(prevState, parentPath);
		var currProps = GetPropertiesAtPath(currState, parentPath);

		foreach (var prop in currProps)
		{
			if (!prevProps.Contains(prop))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Checks if a parent-level change occurred (e.g., new array item was added).
	/// This signals that the current string is complete.
	/// </summary>
	private bool HasParentLevelChange(string path, Dictionary<string, JsonValue> prevState, Dictionary<string, JsonValue> currState)
	{
		var parentPath = GetParentPath(path);
		while (!string.IsNullOrEmpty(parentPath))
		{
			if (parentPath.EndsWith("]", StringComparison.Ordinal))
			{
				var arrayPath = GetParentPath(parentPath);
				if (GetArrayCountAtPath(currState, arrayPath) > GetArrayCountAtPath(prevState, arrayPath))
					return true;
			}

			parentPath = GetParentPath(parentPath);
		}

		return false;
	}

	/// <summary>
	/// Checks if we've already emitted a sibling at the given parent path (for comma management).
	/// </summary>
	private bool HasEmittedSiblingAt(string parentPath)
	{
		var prefix = string.IsNullOrEmpty(parentPath) ? "" : parentPath + ".";

		foreach (var emitted in _emittedPaths)
		{
			if (emitted == parentPath)
				continue;

			if (string.IsNullOrEmpty(parentPath))
			{
				// Root level - check for direct children
				if (!emitted.StartsWith("[", StringComparison.Ordinal) && emitted.IndexOf(".", StringComparison.Ordinal) < 0)
					return true;
			}
			else if (emitted.StartsWith(prefix))
			{
				var remaining = emitted.Substring(prefix.Length);
				// Direct child (no further nesting)
				if (remaining.IndexOf(".", StringComparison.Ordinal) < 0 && remaining.IndexOf("[", StringComparison.Ordinal) < 0)
					return true;
			}
		}

		return false;
	}

	#endregion

	#region Path Utilities

	/// <summary>
	/// Groups string paths by their parent path.
	/// Used to determine if there are multiple strings at the same level.
	/// </summary>
	private static Dictionary<string, List<string>> GroupStringsByParent(Dictionary<string, JsonValue> state)
	{
		var result = new Dictionary<string, List<string>>();

		foreach (var (path, val) in state)
		{
			if (val.Kind == JsonValueKind.String)
			{
				var parent = GetParentPath(path);
				if (!result.TryGetValue(parent, out var list))
				{
					list = new List<string>();
					result[parent] = list;
				}
				list.Add(path);
			}
		}

		return result;
	}

	/// <summary>
	/// Counts growable items (strings, arrays, objects) at a given parent path.
	/// Used to determine if we have multiple ambiguous growable types.
	/// </summary>
	private static int CountGrowablesAtLevel(JsonElement elem, string path)
	{
		if (elem.ValueKind != JsonValueKind.Object)
			return 0;

		int count = 0;
		foreach (var prop in elem.EnumerateObject())
		{
			if (IsGrowable(prop.Value.ValueKind))
				count++;
		}
		return count;
	}

	/// <summary>
	/// Gets the parent path from a full path.
	/// E.g., "days[0].title" → "days[0]", "days[0]" → "days"
	/// </summary>
	private static string GetParentPath(string path)
	{
		var lastDot = path.LastIndexOf('.');
		var lastBracket = path.LastIndexOf('[');

		if (lastDot > lastBracket && lastDot >= 0)
			return path.Substring(0, lastDot);
		if (lastBracket >= 0)
			return path.Substring(0, lastBracket);

		return "";
	}

	/// <summary>
	/// Splits a path into parent and property name.
	/// E.g., "days[0].title" → ("days[0]", "title")
	/// </summary>
	private static (string Parent, string Name) SplitPath(string path)
	{
		var lastDot = path.LastIndexOf('.');
		var lastBracket = path.LastIndexOf('[');

		if (lastDot > lastBracket && lastDot >= 0)
			return (path.Substring(0, lastDot), DecodePathSegment(path.Substring(lastDot + 1)));
		if (lastBracket >= 0)
			return (path.Substring(0, lastBracket), path);
		return ("", DecodePathSegment(path));
	}

	/// <summary>
	/// Combines parent path with child name.
	/// E.g., ("days[0]", "title") → "days[0].title"
	/// </summary>
	private static string CombinePath(string parent, string child) =>
		string.IsNullOrEmpty(parent) ? EncodePathSegment(child) : parent + "." + EncodePathSegment(child);

	// Keep delimiters private to structural paths, including for empty property names.
	private static string EncodePathSegment(string name) => name.Length == 0 ? "~4" :
		name.Replace("~", "~0").Replace(".", "~1").Replace("[", "~2").Replace("]", "~3");

	private static string DecodePathSegment(string name) => name == "~4" ? "" :
		name.Replace("~3", "]").Replace("~2", "[").Replace("~1", ".").Replace("~0", "~");

	private static bool IsDescendant(string path, string parent) =>
		path.StartsWith(parent + ".", StringComparison.Ordinal) || path.StartsWith(parent + "[", StringComparison.Ordinal);

	/// <summary>
	/// Gets all direct property names at a given path.
	/// </summary>
	private static HashSet<string> GetPropertiesAtPath(Dictionary<string, JsonValue> state, string path)
	{
		var props = new HashSet<string>();
		var prefix = string.IsNullOrEmpty(path) ? "" : path + ".";

		foreach (var key in state.Keys)
		{
			if (string.IsNullOrEmpty(path))
			{
				// Root level properties
				if (!key.StartsWith("["))
				{
					var dotIdx = key.IndexOf('.', StringComparison.Ordinal);
					var bracketIdx = key.IndexOf('[', StringComparison.Ordinal);
					var endIdx = dotIdx >= 0 && bracketIdx >= 0 ? Math.Min(dotIdx, bracketIdx) :
						dotIdx >= 0 ? dotIdx : bracketIdx >= 0 ? bracketIdx : key.Length;
					props.Add(key.Substring(0, endIdx));
				}
			}
			else if (key.StartsWith(prefix))
			{
				// Child properties
				var remaining = key.Substring(prefix.Length);
				var dotIdx = remaining.IndexOf('.', StringComparison.Ordinal);
				var bracketIdx = remaining.IndexOf('[', StringComparison.Ordinal);
				var endIdx = dotIdx >= 0 && bracketIdx >= 0 ? Math.Min(dotIdx, bracketIdx) :
					dotIdx >= 0 ? dotIdx : bracketIdx >= 0 ? bracketIdx : remaining.Length;

				if (endIdx > 0)
					props.Add(remaining.Substring(0, endIdx));
			}
		}

		return props;
	}

	/// <summary>
	/// Gets the count of array items at a given array path.
	/// </summary>
	private static int GetArrayCountAtPath(Dictionary<string, JsonValue> state, string path)
	{
		int maxIdx = -1;
		var prefix = path + "[";

		foreach (var key in state.Keys)
		{
			if (key.StartsWith(prefix))
			{
				var bracketEnd = key.IndexOf(']', prefix.Length);
				if (bracketEnd > prefix.Length)
				{
					var idxStr = key.Substring(prefix.Length, bracketEnd - prefix.Length);
					if (int.TryParse(idxStr, out var idx))
						maxIdx = Math.Max(maxIdx, idx);
				}
			}
		}

		return maxIdx + 1;
	}

	#endregion

	#region JSON Flattening

	/// <summary>
	/// Flattens a JSON element into a path→value dictionary.
	/// E.g., {"name": "John", "address": {"city": "NYC"}} becomes:
	///   "name" → "John"
	///   "address.city" → "NYC"
	/// </summary>
	private static Dictionary<string, JsonValue> FlattenJson(JsonElement elem, string path)
	{
		var result = new Dictionary<string, JsonValue>();
		FlattenJsonRecursive(elem, path, result);
		return result;
	}

	/// <summary>
	/// Recursively flattens JSON into path→value entries.
	/// </summary>
	private static void FlattenJsonRecursive(JsonElement elem, string path, Dictionary<string, JsonValue> result)
	{
		switch (elem.ValueKind)
		{
			case JsonValueKind.Object:
				if (!elem.EnumerateObject().Any())
				{
					// Empty object - store it so we know it exists
					result[path] = new JsonValue(JsonValueKind.Object, null, null);
				}
				else
				{
					foreach (var prop in elem.EnumerateObject())
					{
						var propPath = CombinePath(path, prop.Name);
						FlattenJsonRecursive(prop.Value, propPath, result);
					}
				}
				break;

			case JsonValueKind.Array:
				if (elem.GetArrayLength() == 0)
				{
					// Empty array - store it so we know it exists
					result[path] = new JsonValue(JsonValueKind.Array, null, null);
				}
				else
				{
					int i = 0;
					foreach (var item in elem.EnumerateArray())
					{
						FlattenJsonRecursive(item, path + "[" + i + "]", result);
						i++;
					}
				}
				break;

			case JsonValueKind.String:
				result[path] = new JsonValue(JsonValueKind.String, elem.GetString(), null);
				break;

			case JsonValueKind.Number:
				result[path] = new JsonValue(JsonValueKind.Number, null, elem.GetRawText());
				break;

			case JsonValueKind.True:
				result[path] = new JsonValue(JsonValueKind.True, null, null);
				break;

			case JsonValueKind.False:
				result[path] = new JsonValue(JsonValueKind.False, null, null);
				break;

			case JsonValueKind.Null:
				result[path] = new JsonValue(JsonValueKind.Null, null, null);
				break;
		}
	}

	#endregion

	#region String Escaping

	/// <summary>
	/// Escapes a string for JSON output.
	/// </summary>
	private static string Escape(string s)
	{
		var sb = new StringBuilder();

		foreach (var c in s)
		{
			switch (c)
			{
				case '"':
					sb.Append("\\\"");
					break;
				case '\\':
					sb.Append("\\\\");
					break;
				case '\n':
					sb.Append("\\n");
					break;
				case '\r':
					sb.Append("\\r");
					break;
				case '\t':
					sb.Append("\\t");
					break;
				default:
					if (c < 0x20)
						sb.Append($"\\u{(int)c:x4}");
					else
						sb.Append(c);
					break;
			}
		}

		return sb.ToString();
	}

	#endregion
}
