#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Comet.Internal;

internal static class EnvironmentFieldCache
{
	static readonly ConditionalWeakTable<System.Type, (string Field, string Key)[]> Fields = new();

	internal static IReadOnlyList<(string Field, string Key)> Get(View view)
	{
		lock (Fields)
		{
			var type = view.GetType();
			if (Fields.TryGetValue(type, out var cached))
				return cached;

			var fields = view.GetFieldsWithAttribute(typeof(EnvironmentAttribute));
			var result = new (string Field, string Key)[fields.Count];
			for (int i = 0; i < fields.Count; i++)
			{
				var field = fields[i];
				var attribute = field.GetCustomAttributes(true).OfType<EnvironmentAttribute>().First();
				result[i] = (field.Name, attribute.Key ?? field.Name);
			}
			Fields.Add(type, result);
			return result;
		}
	}

	internal static void Clear()
	{
		lock (Fields)
			Fields.Clear();
	}
}
