using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

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
		internal static PropertyInfo GetPropertySafe(this Type type, string name,
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

		public static bool SetPropertyValue<T>(this object obj, string name, T value)
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

		static Action<object, T> CreateSetter<T>(Type type, string name)
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

		public static bool SetPropertyValue(this object obj, string name, object value)
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

		static Action<object, object> CreateSetter(Type type, string name)
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
			var typeName = obj?.GetType().Name;
			if ((typeName == "State`1" || typeName == "Reactive`1") && type.Name != "State`1" && type.Name != "Reactive`1")
			{
				return obj.GetPropValue<object>("Value");
			}
			else if(obj?.GetType().Name == "PropertySubscription`1" && type.Name != "PropertySubscription`1")
			{
				return obj.GetPropValue<object>("CurrentValue");
			}
			//if (type == typeof(String))
			//    return obj.ToString();
			return System.Convert.ChangeType(obj, type);
		}

		public static bool SetDeepPropertyValue(this object obj, string name, object value)
		{
			if (obj is null)
				return false;
			var lastObect = obj;
			FieldInfo field = null;
			PropertyInfo info = null;
			foreach (var part in name.Split('.'))
			{
				if (obj is null)
					return false;
				info = null;
				field = null;
				var type = obj?.GetType();
				lastObect = obj;
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
				field.SetValue(lastObect, value);
				return true;
			}
			else if (info is not null)
			{
				info.SetValue(lastObect, value);
				return true;
			}
			return false;
		}

		public static FieldInfo GetDeepField(this Type type, string name)
		{
			var fieldInfo = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
			if (fieldInfo is null && type.BaseType is not null)
				fieldInfo = GetDeepField(type.BaseType, name);
			return fieldInfo;
		}

		public static PropertyInfo GetDeepProperty(this Type type, string name)
		{
			return type.GetPropertySafe(name);
		}
		public static List<PropertyInfo> GetDeepProperties(this Type type, BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
		{
			var properties = type.GetProperties(flags).ToList();
			if (type.BaseType is not null)
				properties.AddRange(GetDeepProperties(type.BaseType, flags));
			return properties;
		}

		public static MethodInfo GetDeepMethodInfo(this Type type, string name)
		{
			var methodInfo = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
			if (methodInfo is null && type.BaseType is not null)
				methodInfo = GetDeepMethodInfo(type.BaseType, name);
			return methodInfo;
		}
		public static MethodInfo GetDeepMethodInfo(this Type type, Type withAttribute)
		{
			var methodInfo = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Where(m => m.GetCustomAttributes(withAttribute, false).Length > 0).FirstOrDefault();
			if (methodInfo is null && type.BaseType is not null)
				methodInfo = GetDeepMethodInfo(type.BaseType, withAttribute);
			return methodInfo;
		}

		public static object GetPropertyValue(this object obj, string name)
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

		public static T GetPropValue<T>(this object obj, string name)
		{
			var retval = GetPropertyValue(obj, name);
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
