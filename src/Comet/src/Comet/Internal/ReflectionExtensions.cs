using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Comet.Reflection
{
	public static class ReflectionExtensions
	{
		// Cached per (Type, propertyName): null = skip (no writable property/field, or PropertySubscription type),
		// PropertyInfo or FieldInfo = set via this member.
		static readonly Dictionary<(Type, string), MemberInfo> _setMemberCache
			= new Dictionary<(Type, string), MemberInfo>();

		const BindingFlags InstanceBindingFlags =
			BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

		// Walks the type hierarchy using DeclaredOnly so that a `new` (shadowing)
		// property with a different return type doesn't raise AmbiguousMatchException.
		// The most-derived declaration wins, matching C# member-lookup semantics.
		[UnconditionalSuppressMessage(
			"Trimming",
			"IL2075",
			Justification = "NativeAOT callers pass View runtime types whose class-level contract preserves properties throughout the inheritance chain; the linker does not propagate that contract through Type.BaseType.")]
		internal static PropertyInfo GetPropertySafe(
			[DynamicallyAccessedMembers(
				DynamicallyAccessedMemberTypes.PublicProperties |
				DynamicallyAccessedMemberTypes.NonPublicProperties)] this Type type,
			string name,
			BindingFlags flags = InstanceBindingFlags)
		{
			var declaredFlags = flags | BindingFlags.DeclaredOnly;
			for (var t = type; t is not null; t = t.BaseType)
			{
				try
				{
					var info = t.GetProperty(name, declaredFlags);
					if (info is not null)
						return info;
				}
				catch (AmbiguousMatchException)
				{
					// Two declarations at the same level with the same name —
					// pick the one whose declaring type is this level.
					var match = t.GetProperties(declaredFlags)
						.FirstOrDefault(p => p.Name == name && p.DeclaringType == t);
					if (match is not null)
						return match;
				}
			}
			return null;
		}

		[RequiresUnreferencedCode(
			"Setting members on arbitrary objects requires runtime property and field metadata. " +
			"Use the View overload for Comet views in trimmed applications.")]
		public static bool SetPropertyValue<T>(this object obj, string name, T value)
		{
			if (obj is View view)
				return SetPropertyValue(view, name, value);

			var type = obj.GetType();
			var key = (type, name);
			var setter = SetterCache<T>.Get(key);
			if (setter is not null)
			{
				setter(obj, value);
				return true;
			}

			if (!SetterCache<T>.Has(key))
			{
				var created = CreateSetter<T>(type, name);
				SetterCache<T>.Set(key, created);
				if (created is not null)
				{
					created(obj, value);
					return true;
				}
			}

			return SetPropertyValue(obj, name, (object)value);
		}

		public static bool SetPropertyValue<T>(this View obj, string name, T value)
		{
			var type = obj.GetType();
			var key = (type, name);

			// Fast path using compiled delegates
			var setter = SetterCache<T>.Get(key);
			if (setter is not null)
			{
				setter(obj, value);
				return true;
			}
			
			// Fallback or first run
			if (!SetterCache<T>.Has(key))
			{
				var s = CreateSetter<T>(type, name);
				SetterCache<T>.Set(key, s);
				if (s is not null)
				{
					s(obj, value);
					return true;
				}
			}

			return SetPropertyValue(obj, name, (object)value);
		}

		static class SetterCache<T>
		{
			static readonly Dictionary<(Type, string), Action<object, T>> Cache = new Dictionary<(Type, string), Action<object, T>>();
			public static Action<object, T> Get((Type, string) key) => Cache.TryGetValue(key, out var action) ? action : null;
			public static void Set((Type, string) key, Action<object, T> action) => Cache[key] = action;
			public static bool Has((Type, string) key) => Cache.ContainsKey(key);
		}

		static Action<object, T> CreateSetter<T>(
			[DynamicallyAccessedMembers(
				DynamicallyAccessedMemberTypes.PublicFields |
				DynamicallyAccessedMemberTypes.NonPublicFields |
				DynamicallyAccessedMemberTypes.PublicProperties |
				DynamicallyAccessedMemberTypes.NonPublicProperties)] Type type,
			string name)
		{
			var property = type.GetPropertySafe(name);
			if (property is not null && property.CanWrite)
			{
				if (property.PropertyType.IsDeepSubclass(typeof(Comet.Reactive.PropertySubscription<>)))
					return null;

				var target = Expression.Parameter(typeof(object), "target");
				var value = Expression.Parameter(typeof(T), "value");
				var castTarget = Expression.Convert(target, type);
				
				Expression body = null;
				if (property.PropertyType == typeof(T))
				{
					body = Expression.Call(castTarget, property.GetSetMethod(true), value);
				}
				else
				{
					try
					{
						var converted = Expression.Convert(value, property.PropertyType);
						body = Expression.Call(castTarget, property.GetSetMethod(true), converted);
					}
					catch
					{
						// Conversion not supported by Expression.Convert
						return null;
					}
				}
				
				return Expression.Lambda<Action<object, T>>(body, target, value).Compile();
			}
			
			var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
			if (field is not null)
			{
				var target = Expression.Parameter(typeof(object), "target");
				var value = Expression.Parameter(typeof(T), "value");
				var castTarget = Expression.Convert(target, type);

				Expression body = null;
				if (field.FieldType == typeof(T))
				{
					body = Expression.Assign(Expression.Field(castTarget, field), value);
				}
				else
				{
					try
					{
						var converted = Expression.Convert(value, field.FieldType);
						body = Expression.Assign(Expression.Field(castTarget, field), converted);
					}
					catch
					{
						return null;
					}
				}

				return Expression.Lambda<Action<object, T>>(body, target, value).Compile();
			}

			return null;
		}

		static readonly Dictionary<(Type, string), Action<object, object>> _setterCache = new Dictionary<(Type, string), Action<object, object>>();

		[RequiresUnreferencedCode(
			"Setting members on arbitrary objects requires runtime property and field metadata. " +
			"Use the View overload for Comet views in trimmed applications.")]
		public static bool SetPropertyValue(this object obj, string name, object value)
		{
			if (obj is View view)
				return SetPropertyValue(view, name, value);

			var type = obj.GetType();
			var cacheKey = (type, name);

			if (_setterCache.TryGetValue(cacheKey, out var setter))
			{
				if (setter is not null)
				{
					setter(obj, value);
					return true;
				}
			}
			else
			{
				var created = CreateSetter(type, name);
				_setterCache[cacheKey] = created;
				if (created is not null)
				{
					created(obj, value);
					return true;
				}
			}

			if (!_setMemberCache.TryGetValue(cacheKey, out var member))
			{
				var info = type.GetPropertySafe(name);
				if (info is not null && info.CanWrite)
				{
					if (info.PropertyType.IsDeepSubclass(typeof(Comet.Reactive.PropertySubscription<>)))
						member = null;
					else
						member = info;
				}
				else
				{
					member = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
				}
				_setMemberCache[cacheKey] = member;
			}

			if (member is PropertyInfo property)
			{
				property.SetValue(obj, Convert(value, property.PropertyType));
				return true;
			}

			if (member is FieldInfo field)
			{
				field.SetValue(obj, Convert(value, field.FieldType));
				return true;
			}

			return false;
		}

		public static bool SetPropertyValue(this View obj, string name, object value)
		{
			var type = obj.GetType();
			var cacheKey = (type, name);

			if (_setterCache.TryGetValue(cacheKey, out var setter))
			{
				if (setter is not null)
				{
					setter(obj, value);
					return true;
				}
			}
			else
			{
				var s = CreateSetter(type, name);
				_setterCache[cacheKey] = s;
				if (s is not null)
				{
					s(obj, value);
					return true;
				}
			}

			if (!_setMemberCache.TryGetValue(cacheKey, out var member))
			{
				// First call for this (Type, name) — resolve and cache
				var info = type.GetPropertySafe(name);
				if (info is not null && info.CanWrite)
				{
					if (info.PropertyType.IsDeepSubclass(typeof(Comet.Reactive.PropertySubscription<>)))
						member = null; // PropertySubscription-typed — always skip
					else
						member = info;
				}
				else
				{
					var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
					member = field; // null if not found
				}
				_setMemberCache[cacheKey] = member;
			}

			if (member is null)
				return false;

			if (member is PropertyInfo pi)
			{
				pi.SetValue(obj, Convert(value, pi.PropertyType));
				return true;
			}
			else if (member is FieldInfo fi)
			{
				fi.SetValue(obj, Convert(value, fi.FieldType));
				return true;
			}
			return false;
		}

		static Action<object, object> CreateSetter(
			[DynamicallyAccessedMembers(
				DynamicallyAccessedMemberTypes.PublicFields |
				DynamicallyAccessedMemberTypes.NonPublicFields |
				DynamicallyAccessedMemberTypes.PublicProperties |
				DynamicallyAccessedMemberTypes.NonPublicProperties)] Type type,
			string name)
		{
			try
			{
				var property = type.GetPropertySafe(name);
				if (property is not null && property.CanWrite)
				{
					if (property.PropertyType.IsDeepSubclass(typeof(Comet.Reactive.PropertySubscription<>)))
						return null;

					var target = Expression.Parameter(typeof(object), "target");
					var value = Expression.Parameter(typeof(object), "value");
					var castTarget = Expression.Convert(target, type);
					
					var convertMethod = typeof(ReflectionExtensions).GetMethod("Convert", new[] { typeof(object), typeof(Type) });
					var convertedValue = Expression.Call(convertMethod, value, Expression.Constant(property.PropertyType));
					var castConvertedValue = Expression.Convert(convertedValue, property.PropertyType);

					var method = property.GetSetMethod(true);
					if (method is not null)
					{
						var body = Expression.Call(castTarget, method, castConvertedValue);
						return Expression.Lambda<Action<object, object>>(body, target, value).Compile();
					}
				}
				
				var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
				if (field is not null)
				{
					var target = Expression.Parameter(typeof(object), "target");
					var value = Expression.Parameter(typeof(object), "value");
					var castTarget = Expression.Convert(target, type);
					
					var convertMethod = typeof(ReflectionExtensions).GetMethod("Convert", new[] { typeof(object), typeof(Type) });
					var convertedValue = Expression.Call(convertMethod, value, Expression.Constant(field.FieldType));
					var castConvertedValue = Expression.Convert(convertedValue, field.FieldType);

					var body = Expression.Assign(Expression.Field(castTarget, field), castConvertedValue);
					return Expression.Lambda<Action<object, object>>(body, target, value).Compile();
				}
			}
			catch
			{
				// Ignore errors in expression generation, fallback to reflection
			}
			return null;
		}

		public static T Convert<T>(this object obj) => (T)obj.Convert(typeof(T));

		public static object Convert(this object obj, Type type)
		{
			if (obj is null)
				return null;
			var newType = obj.GetType();
			if (type.IsAssignableFrom(newType))
				return obj;
			if (obj is Comet.Reactive.IUntypedValue valueSource &&
				!type.IsInstanceOfType(obj))
			{
				return valueSource.UntypedValue;
			}
			//if (type == typeof(String))
			//    return obj.ToString();
			return System.Convert.ChangeType(obj, type);
		}

		[RequiresUnreferencedCode(
			"Setting nested members on arbitrary objects requires runtime property and field metadata. " +
			"Use SetDirectPropertyValue for direct Comet View members in trimmed applications.")]
		public static bool SetDeepPropertyValue(this object obj, string name, object value)
		{
			if (obj is View view)
				return SetDeepPropertyValue(view, name, value);
			if (obj is null)
				return false;
			return SetNestedPropertyValue(obj, name, value);
		}

		/// <summary>Sets a View member or a nested property path. Nested object metadata must be preserved.</summary>
		[RequiresUnreferencedCode(
			"Nested property paths require runtime property and field metadata. " +
			"Use SetDirectPropertyValue for direct Comet View members in trimmed applications.")]
		public static bool SetDeepPropertyValue(this View obj, string name, object value)
		{
			if (obj is null)
				return false;

			if (!name.Contains('.'))
				return SetDirectPropertyValue(obj, name, value);

			return SetNestedPropertyValue(obj, name, value);
		}

		/// <summary>Sets a direct View property or field without conversion. Nested paths are not supported.</summary>
		public static bool SetDirectPropertyValue(this View obj, string name, object value)
		{
			if (obj is null)
				return false;
			if (name.Contains('.'))
				throw new ArgumentException("A direct View member name cannot contain a nested path.", nameof(name));

			var type = obj.GetType();
			var property = type.GetDeepProperty(name);
			if (property is not null)
			{
				property.SetValue(obj, value);
				return true;
			}

			var field = type.GetDeepField(name);
			if (field is null)
				return false;

			field.SetValue(obj, value);
			return true;
		}

		[RequiresUnreferencedCode(
			"Nested property paths require runtime property and field metadata.")]
		static bool SetNestedPropertyValue(object obj, string name, object value)
		{
			var lastObject = obj;
			FieldInfo field = null;
			PropertyInfo info = null;
			foreach (var part in name.Split('.'))
			{
				if (obj is null)
					return false;
				info = null;
				field = null;
				var type = obj?.GetType();
				lastObject = obj;
				info = type?.GetDeepProperty(part);
				if (info is not null)
				{
					obj = info.GetValue(obj, null);
				}
				else
				{
					field = type?.GetDeepField(part);
					if (field is null)
						return false;
					obj = field.GetValue(obj);
				}
			}
			if (field is not null)
			{
				field.SetValue(lastObject, value);
				return true;
			}
			else if (info is not null)
			{
				info.SetValue(lastObject, value);
				return true;
			}
			return false;
		}

		[UnconditionalSuppressMessage(
			"Trimming",
			"IL2072",
			Justification = "NativeAOT callers pass View runtime types whose class-level contract preserves fields throughout the inheritance chain; the linker does not propagate that contract through Type.BaseType.")]
		public static FieldInfo GetDeepField(
			[DynamicallyAccessedMembers(
				DynamicallyAccessedMemberTypes.PublicFields |
				DynamicallyAccessedMemberTypes.NonPublicFields)] this Type type,
			string name)
		{
			var fieldInfo = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
			if (fieldInfo is null && type.BaseType is not null)
				fieldInfo = GetDeepField(type.BaseType, name);
			return fieldInfo;
		}

		public static PropertyInfo GetDeepProperty(
			[DynamicallyAccessedMembers(
				DynamicallyAccessedMemberTypes.PublicProperties |
				DynamicallyAccessedMemberTypes.NonPublicProperties)] this Type type,
			string name)
		{
			return type.GetPropertySafe(name);
		}
		public static List<PropertyInfo> GetDeepProperties(
			[DynamicallyAccessedMembers(
				DynamicallyAccessedMemberTypes.PublicProperties |
				DynamicallyAccessedMemberTypes.NonPublicProperties)] this Type type,
			BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
		{
			var properties = type.GetProperties(flags).ToList();
			if (type.BaseType is not null)
				properties.AddRange(GetDeepProperties(type.BaseType, flags));
			return properties;
		}

		[UnconditionalSuppressMessage(
			"Trimming",
			"IL2072",
			Justification = "NativeAOT callers pass View runtime types whose class-level contract preserves methods throughout the inheritance chain; the linker does not propagate that contract through Type.BaseType.")]
		public static MethodInfo GetDeepMethodInfo(
			[DynamicallyAccessedMembers(
				DynamicallyAccessedMemberTypes.PublicMethods |
				DynamicallyAccessedMemberTypes.NonPublicMethods)] this Type type,
			string name)
		{
			var methodInfo = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
			if (methodInfo is null && type.BaseType is not null)
				methodInfo = GetDeepMethodInfo(type.BaseType, name);
			return methodInfo;
		}

		[UnconditionalSuppressMessage(
			"Trimming",
			"IL2072",
			Justification = "NativeAOT callers pass View runtime types whose class-level contract preserves non-public methods throughout the inheritance chain; the linker does not propagate that contract through Type.BaseType.")]
		internal static MethodInfo GetDeepNonPublicMethodInfo(
			[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicMethods)] this Type type,
			Type withAttribute)
		{
			var methodInfo = type
				.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
				.FirstOrDefault(method => method.GetCustomAttributes(withAttribute, false).Length > 0);
			if (methodInfo is null && type.BaseType is not null)
				methodInfo = GetDeepNonPublicMethodInfo(type.BaseType, withAttribute);
			return methodInfo;
		}
		[UnconditionalSuppressMessage(
			"Trimming",
			"IL2072",
			Justification = "NativeAOT callers pass View runtime types whose class-level contract preserves methods throughout the inheritance chain; the linker does not propagate that contract through Type.BaseType.")]
		public static MethodInfo GetDeepMethodInfo(
			[DynamicallyAccessedMembers(
				DynamicallyAccessedMemberTypes.PublicMethods |
				DynamicallyAccessedMemberTypes.NonPublicMethods)] this Type type,
			Type withAttribute)
		{
			var methodInfo = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Where(m => m.GetCustomAttributes(withAttribute, false).Length > 0).FirstOrDefault();
			if (methodInfo is null && type.BaseType is not null)
				methodInfo = GetDeepMethodInfo(type.BaseType, withAttribute);
			return methodInfo;
		}

		[RequiresUnreferencedCode(
			"Reading members on arbitrary objects requires runtime property and field metadata. " +
			"Use GetDirectPropertyValue for direct Comet View members in trimmed applications.")]
		public static object GetPropertyValue(this object obj, string name)
		{
			if (obj is View view)
				return GetPropertyValue(view, name);
			return GetNestedPropertyValue(obj, name);
		}

		/// <summary>Reads a View member or a nested property path. Nested object metadata must be preserved.</summary>
		[RequiresUnreferencedCode(
			"Nested property paths require runtime property and field metadata. " +
			"Use GetDirectPropertyValue for direct Comet View members in trimmed applications.")]
		public static object GetPropertyValue(this View obj, string name)
		{
			if (obj is null)
				return null;

			if (!name.Contains('.'))
				return GetDirectPropertyValue(obj, name);

			return GetNestedPropertyValue(obj, name);
		}

		/// <summary>Reads a direct View property or field. Nested paths are not supported.</summary>
		public static object GetDirectPropertyValue(this View obj, string name)
		{
			if (obj is null)
				return null;
			if (name.Contains('.'))
				throw new ArgumentException("A direct View member name cannot contain a nested path.", nameof(name));

			var type = obj.GetType();
			var info = type.GetDeepProperty(name);
			if (info is not null)
				return info.GetValue(obj, null);

			return type.GetDeepField(name)?.GetValue(obj);
		}

		[RequiresUnreferencedCode(
			"Nested property paths require runtime property and field metadata.")]
		static object GetNestedPropertyValue(object obj, string name)
		{
			foreach (var part in name.Split('.'))
			{
				if (obj is null)
					return null;
				var type = obj.GetType();
				var info = type.GetPropertySafe(part);
				if (info is not null)
				{
					obj = info.GetValue(obj, null);
				}
				else
				{
					var field = type.GetField(part, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
					if (field is null)
						return null;
					obj = field.GetValue(obj);
				}
			}
			return obj;
		}

		[RequiresUnreferencedCode(
			"Reading members on arbitrary objects requires runtime property and field metadata. " +
			"Use GetDirectPropValue for direct Comet View members in trimmed applications.")]
		public static T GetPropValue<T>(this object obj, string name)
		{
			var retval = GetPropertyValue(obj, name);
			if (retval is null)
				return default;
			return (T)retval;
		}

		[RequiresUnreferencedCode(
			"Nested property paths require runtime property and field metadata. " +
			"Use GetDirectPropValue for direct Comet View members in trimmed applications.")]
		public static T GetPropValue<T>(this View obj, string name)
		{
			var retval = GetPropertyValue(obj, name);
			if (retval is null)
				return default;
			return (T)retval;
		}

		[RequiresUnreferencedCode(
			"Reading members on arbitrary IView implementations requires runtime property and field metadata. " +
			"Use GetDirectPropValue for direct Comet View members in trimmed applications.")]
		public static T GetPropValue<T>(this Microsoft.Maui.IView obj, string name)
			=> GetPropValue<T>((object)obj, name);

		/// <summary>Reads a direct View property or field, returning default when absent or null.</summary>
		public static T GetDirectPropValue<T>(this View obj, string name)
		{
			var retval = GetDirectPropertyValue(obj, name);
			if (retval is null)
				return default;
			return (T)retval;
		}

		public static bool IsDeepSubclass(this Type type, Type subclass)
		{
			if (type.IsSubclassOf(subclass))
				return true;
			// Handle open generic type definitions (e.g. typeof(PropertySubscription<>))
			if (subclass.IsGenericTypeDefinition && type.IsGenericType && type.GetGenericTypeDefinition() == subclass)
				return true;
			return type?.BaseType?.IsDeepSubclass(subclass) ?? false;
		}
	}
}
