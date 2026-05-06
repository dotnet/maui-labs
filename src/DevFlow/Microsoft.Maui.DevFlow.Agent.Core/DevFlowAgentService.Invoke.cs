using System.ComponentModel;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Text.Json;

[assembly: MetadataUpdateHandler(typeof(Microsoft.Maui.DevFlow.Agent.Core.DevFlowActionHotReloadHandler))]

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Handles C# Hot Reload / Edit and Continue metadata updates.
/// When types are modified at runtime, this invalidates the cached
/// DevFlowAction list so newly added [DevFlowAction] methods are
/// immediately discoverable by AI agents.
/// </summary>
static class DevFlowActionHotReloadHandler
{
	/// <summary>Called by the runtime after a metadata update (Hot Reload / EnC).</summary>
	internal static void UpdateApplication(Type[]? updatedTypes)
	{
		// Any metadata update could have added/removed/changed [DevFlowAction] methods.
		// Invalidate so the next DiscoverActions() call rescans.
		DevFlowAgentService.InvalidateActionCache();
	}
}

// Invoke / reflection endpoints
public partial class DevFlowAgentService
{
	private static volatile Lazy<InvokeActionEntry[]> s_cachedActions = new(ScanActions, LazyThreadSafetyMode.ExecutionAndPublication);

	#region Action Discovery

	private InvokeActionEntry[] DiscoverActions() => s_cachedActions.Value;

	/// <summary>
	/// Invalidates cached action metadata. The next call to DiscoverActions()
	/// will rescan all loaded assemblies.
	/// Called by AssemblyLoad handler and MetadataUpdateHandler (Hot Reload).
	/// </summary>
	internal static void InvalidateActionCache()
	{
		s_cachedActions = new Lazy<InvokeActionEntry[]>(ScanActions, LazyThreadSafetyMode.ExecutionAndPublication);
	}

	private void OnAssemblyLoaded(object? sender, AssemblyLoadEventArgs args)
	{
		if (args.LoadedAssembly.IsDynamic)
			return;

		InvalidateActionCache();
	}

	private static InvokeActionEntry[] ScanActions()
	{
		var actions = new List<InvokeActionEntry>();

		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			if (asm.IsDynamic)
				continue;

			Type[] types;
			try { types = asm.GetTypes(); }
			catch { continue; }

			foreach (var type in types)
			{
				foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
				{
					var attr = method.GetCustomAttribute<DevFlowActionAttribute>();
					if (attr == null) continue;

					actions.Add(new InvokeActionEntry
					{
						Name = attr.Name,
						Description = attr.Description,
						DeclaringType = type.FullName ?? type.Name,
						Method = method,
						Parameters = BuildParameterInfoList(method)
					});
				}
			}
		}

		// Detect and deduplicate shadowed action names (keep the preferred occurrence)
		var duplicates = actions
			.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
			.Where(g => g.Count() > 1);

		foreach (var group in duplicates)
		{
			var preferred = SelectPreferredAction(group);
			var shadowed = group.Where(a => !ReferenceEquals(a, preferred));
			foreach (var dup in shadowed)
			{
				System.Diagnostics.Debug.WriteLine(
					$"[Microsoft.Maui.DevFlow] Warning: Duplicate DevFlowAction name '{group.Key}' on {dup.DeclaringType}.{dup.Method.Name} shadows the preferred registration on {preferred.DeclaringType}.{preferred.Method.Name}. The duplicate will be ignored.");
			}
		}

		return actions
			.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
			.Select(SelectPreferredAction)
			.ToArray();
	}

	private static InvokeActionEntry SelectPreferredAction(IEnumerable<InvokeActionEntry> actions)
		=> actions
			.OrderByDescending(a => AssemblyLoadContext.GetLoadContext(a.Method.DeclaringType!.Assembly) == AssemblyLoadContext.Default)
			.ThenByDescending(a => IsAssemblyInAppBaseDirectory(a.Method.DeclaringType!.Assembly))
			.ThenBy(a => a.Method.DeclaringType!.Assembly.FullName, StringComparer.Ordinal)
			.ThenBy(a => a.DeclaringType, StringComparer.Ordinal)
			.ThenBy(a => a.Method.MetadataToken)
			.First();

	private static bool IsAssemblyInAppBaseDirectory(Assembly assembly)
	{
		var location = assembly.Location;
		if (string.IsNullOrEmpty(location))
			return false;

		return string.Equals(
			Path.GetFullPath(Path.GetDirectoryName(location) ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar),
			Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar),
			StringComparison.OrdinalIgnoreCase);
	}

	private static InvokeParameterInfo[] BuildParameterInfoList(MethodInfo method)
	{
		return method.GetParameters().Select(p => new InvokeParameterInfo
		{
			Name = p.Name ?? "arg",
			Type = FormatParameterTypeName(p.ParameterType),
			Description = p.GetCustomAttribute<DescriptionAttribute>()?.Description,
			DefaultValue = p.HasDefaultValue ? FormatDefaultValue(p.DefaultValue) : null,
			IsRequired = !p.HasDefaultValue && Nullable.GetUnderlyingType(p.ParameterType) == null
		}).ToArray();
	}

	private static string FormatParameterTypeName(Type type)
	{
		var underlying = Nullable.GetUnderlyingType(type);
		if (underlying != null)
			return FormatParameterTypeName(underlying) + "?";

		if (type.IsArray)
			return FormatParameterTypeName(type.GetElementType()!) + "[]";

		if (type.IsGenericType)
		{
			var def = type.GetGenericTypeDefinition();
			if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>))
				return FormatParameterTypeName(type.GetGenericArguments()[0]) + "[]";
		}

		return Type.GetTypeCode(type) switch
		{
			TypeCode.String => "string",
			TypeCode.Boolean => "bool",
			TypeCode.Int32 => "int",
			TypeCode.Int64 => "long",
			TypeCode.Int16 => "short",
			TypeCode.Byte => "byte",
			TypeCode.Single => "float",
			TypeCode.Double => "double",
			TypeCode.Decimal => "decimal",
			_ => type.IsEnum ? $"enum({type.Name})" : type.Name
		};
	}

	private static string? FormatDefaultValue(object? value)
	{
		if (value == null) return "null";
		if (value is string s) return s;
		if (value is bool b) return b ? "true" : "false";
		return value.ToString();
	}

	#endregion

	#region Parameter Conversion

	private static object? ConvertInvokeArg(Type targetType, JsonElement argElement)
	{
		var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

		// Null handling
		if (argElement.ValueKind == JsonValueKind.Null)
		{
			if (Nullable.GetUnderlyingType(targetType) != null || !targetType.IsValueType)
				return null;
			throw new ArgumentException($"Cannot pass null for non-nullable type {targetType.Name}");
		}

		// String
		if (underlying == typeof(string))
			return argElement.GetString();

		// Boolean
		if (underlying == typeof(bool))
		{
			if (argElement.ValueKind == JsonValueKind.True || argElement.ValueKind == JsonValueKind.False)
				return argElement.GetBoolean();
			if (argElement.ValueKind != JsonValueKind.String)
				throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");
			var str = argElement.GetString()
				?? throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");
			return bool.Parse(str);
		}

		// Integer types
		if (underlying == typeof(int)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetInt32() : argElement.ValueKind == JsonValueKind.String ? int.Parse(argElement.GetString()!) : throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");
		if (underlying == typeof(long)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetInt64() : argElement.ValueKind == JsonValueKind.String ? long.Parse(argElement.GetString()!) : throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");
		if (underlying == typeof(short)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetInt16() : argElement.ValueKind == JsonValueKind.String ? short.Parse(argElement.GetString()!) : throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");
		if (underlying == typeof(byte)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetByte() : argElement.ValueKind == JsonValueKind.String ? byte.Parse(argElement.GetString()!) : throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");

		// Floating point
		if (underlying == typeof(float)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetSingle() : argElement.ValueKind == JsonValueKind.String ? float.Parse(argElement.GetString()!) : throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");
		if (underlying == typeof(double)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetDouble() : argElement.ValueKind == JsonValueKind.String ? double.Parse(argElement.GetString()!) : throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");
		if (underlying == typeof(decimal)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetDecimal() : argElement.ValueKind == JsonValueKind.String ? decimal.Parse(argElement.GetString()!) : throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");

		// Enums
		if (underlying.IsEnum)
		{
			if (argElement.ValueKind == JsonValueKind.String)
			{
				var s = argElement.GetString() ?? throw new ArgumentException($"Cannot convert null string to {underlying.Name}");
				return Enum.Parse(underlying, s, ignoreCase: true);
			}
			if (argElement.ValueKind == JsonValueKind.Number)
				return Enum.ToObject(underlying, argElement.GetInt64());
			throw new ArgumentException($"Cannot convert {argElement.ValueKind} to enum {underlying.Name}");
		}

		// Arrays and lists
		if (argElement.ValueKind == JsonValueKind.Array)
		{
			Type? elementType = null;

			if (underlying.IsArray)
				elementType = underlying.GetElementType()!;
			else if (underlying.IsGenericType)
			{
				var def = underlying.GetGenericTypeDefinition();
				if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>) || def == typeof(ICollection<>) || def == typeof(IReadOnlyCollection<>))
					elementType = underlying.GetGenericArguments()[0];
			}

			if (elementType != null)
			{
				var items = new List<object?>();
				foreach (var item in argElement.EnumerateArray())
					items.Add(ConvertInvokeArg(elementType, item));

				if (underlying.IsArray)
				{
					var arr = Array.CreateInstance(elementType, items.Count);
					for (int i = 0; i < items.Count; i++)
						arr.SetValue(items[i], i);
					return arr;
				}

				var listType = typeof(List<>).MakeGenericType(elementType);
				var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
				foreach (var item in items)
					list.Add(item);
				return list;
			}
		}

		// Fallback: treat as string
		if (argElement.ValueKind == JsonValueKind.String)
			return argElement.GetString();

		throw new ArgumentException($"Cannot convert JSON {argElement.ValueKind} to {targetType.Name}");
	}

	private static object?[] ConvertInvokeArgs(ParameterInfo[] parameters, JsonElement[]? args)
	{
		var result = new object?[parameters.Length];
		for (int i = 0; i < parameters.Length; i++)
		{
			if (args != null && i < args.Length)
			{
				result[i] = ConvertInvokeArg(parameters[i].ParameterType, args[i]);
			}
			else if (parameters[i].HasDefaultValue)
			{
				result[i] = parameters[i].DefaultValue;
			}
			else
			{
				throw new ArgumentException($"Missing required argument '{parameters[i].Name}' (parameter {i})");
			}
		}
		return result;
	}

	#endregion

	#region Invoke Execution

	private static async Task<(bool success, string? returnValue, string? returnType, string? error)> InvokeMethodAsync(
		MethodInfo method, object? target, object?[] args)
	{
		try
		{
			var result = method.Invoke(target, args);

			// Handle ValueTask (struct — does not inherit from Task)
			if (result is ValueTask vt)
			{
				await vt;
				return (true, null, "void", null);
			}

			// Handle ValueTask<T> via reflection (generic struct)
			var resultType = result?.GetType();
			if (resultType != null && resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(ValueTask<>))
			{
				var asTaskMethod = resultType.GetMethod("AsTask");
				var task2 = (Task)asTaskMethod!.Invoke(result, null)!;
				await task2;
				var resultProp = task2.GetType().GetProperty("Result");
				var taskResult = resultProp?.GetValue(task2);
				var innerType = resultType.GetGenericArguments()[0];
				return (true, taskResult != null ? FormatPropertyValue(taskResult) : null, FormatParameterTypeName(innerType), null);
			}

			// Handle async methods
			if (result is Task task)
			{
				await task;

				var taskType = task.GetType();
				if (taskType.IsGenericType)
				{
					// Task<T> — unwrap the result
					var resultProp = taskType.GetProperty("Result");
					var taskResult = resultProp?.GetValue(task);
					return (true, FormatPropertyValue(taskResult), FormatParameterTypeName(taskType.GetGenericArguments()[0]), null);
				}

				return (true, null, "void", null);
			}

			// Void method
			if (method.ReturnType == typeof(void))
				return (true, null, "void", null);

			// Synchronous return value
			return (true, FormatPropertyValue(result), FormatParameterTypeName(method.ReturnType), null);
		}
		catch (TargetInvocationException tie)
		{
			var inner = tie.InnerException ?? tie;
			return (false, null, null, $"{inner.GetType().Name}: {inner.Message}");
		}
		catch (Exception ex)
		{
			return (false, null, null, $"{ex.GetType().Name}: {ex.Message}");
		}
	}

	private async Task<InvokeMethodResult> DispatchInvokeMethodAsync(MethodInfo method, object? target, object?[] args)
	{
		// Force the async DispatchAsync overload so invoke continuations are awaited by the dispatcher callback.
		var result = await DispatchAsync<InvokeMethodResult>(async () =>
		{
			var (success, returnValue, returnType, error) = await InvokeMethodAsync(method, target, args);
			return new InvokeMethodResult(success, returnValue, returnType, error);
		});
		return result!;
	}

	#endregion

	#region HTTP Handlers

	private static HttpResponse InvokeError(string error) =>
		HttpResponse.Error(error);

	private Task<HttpResponse> HandleListActions(HttpRequest request)
	{
		var actions = DiscoverActions();
		var result = actions.Select(a => new
		{
			name = a.Name,
			description = a.Description,
			declaringType = a.DeclaringType,
			parameters = a.Parameters.Select(p => new
			{
				name = p.Name,
				type = p.Type,
				description = p.Description,
				defaultValue = p.DefaultValue,
				isRequired = p.IsRequired
			})
		});
		return Task.FromResult(HttpResponse.Json(new { actions = result }));
	}

	private async Task<HttpResponse> HandleInvokeAction(HttpRequest request)
	{
		if (!request.RouteParams.TryGetValue("name", out var actionName))
			return InvokeError("Action name required");

		var actions = DiscoverActions();
		var action = Array.Find(actions, a => string.Equals(a.Name, actionName, StringComparison.OrdinalIgnoreCase));
		if (action == null)
			return InvokeError($"Action '{actionName}' not found. Use GET /api/v1/invoke/actions to list available actions.");

		JsonElement[]? args = null;
		if (request.Body != null)
		{
			var body = request.BodyAs<InvokeActionRequest>();
			args = body?.Args;
		}

		try
		{
			var convertedArgs = ConvertInvokeArgs(action.Method.GetParameters(), args);
			var (success, returnValue, returnType, error) = await DispatchInvokeMethodAsync(action.Method, null, convertedArgs);

			return success
				? HttpResponse.Json(new { success = true, action = action.Name, returnValue, returnType })
				: InvokeError($"Action '{actionName}' failed: {error}");
		}
		catch (Exception ex)
		{
			return InvokeError($"Argument error: {ex.Message}");
		}
	}

	#endregion

	#region DTOs

	private class InvokeActionEntry
	{
		public string Name { get; set; } = "";
		public string? Description { get; set; }
		public string DeclaringType { get; set; } = "";
		public MethodInfo Method { get; set; } = null!;
		public InvokeParameterInfo[] Parameters { get; set; } = [];
	}

	private class InvokeParameterInfo
	{
		public string Name { get; set; } = "";
		public string Type { get; set; } = "";
		public string? Description { get; set; }
		public string? DefaultValue { get; set; }
		public bool IsRequired { get; set; }
	}

	private sealed record InvokeMethodResult(bool Success, string? ReturnValue, string? ReturnType, string? Error);

	#endregion
}

public class InvokeActionRequest
{
	public JsonElement[]? Args { get; set; }
}
