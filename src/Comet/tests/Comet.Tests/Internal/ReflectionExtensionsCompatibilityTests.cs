using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Comet.Reflection;
using Microsoft.Maui;
using Xunit;

namespace Comet.Tests
{
	public class ReflectionExtensionsCompatibilityTests : TestBase
	{
		[AttributeUsage(AttributeTargets.Field)]
		sealed class MarkedFieldAttribute : Attribute
		{
		}

		sealed class NestedValue
		{
			public string Name { get; set; }
		}

		sealed class PlainObject
		{
			[MarkedField]
			public int Count;
			public string Title { get; set; }
			public NestedValue Nested { get; } = new NestedValue();
		}

		class BaseMemberView : View
		{
			int inheritedCount = 1;
			string InheritedTitle { get; set; } = "Base";
		}

		sealed class MemberView : BaseMemberView
		{
			public int Count = 1;
			public string Title { get; set; } = "Initial";
			public NestedValue Nested { get; set; } = new NestedValue();
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void LegacyViewNestedPaths_PreserveGetterAndSetterBehavior(bool useObjectOverload)
		{
			using var view = new MemberView();
			const string path = "Nested.Name";

			Assert.True(useObjectOverload
				? ((object)view).SetDeepPropertyValue(path, "Nested")
				: view.SetDeepPropertyValue(path, "Nested"));
			Assert.Equal("Nested", useObjectOverload
				? ((object)view).GetPropertyValue(path)
				: view.GetPropertyValue(path));
			Assert.Equal("Nested", view.GetPropValue<string>(path));
			Assert.Equal("Nested", ((IView)view).GetPropValue<string>(path));
			Assert.Equal("Nested", ((object)view).GetPropValue<string>(path));

			view.Nested = null;
			Assert.Null(view.GetPropertyValue(path));
			Assert.False(view.SetDeepPropertyValue(path, "Ignored"));
		}

		[Theory]
		[InlineData("Count", 42)]
		[InlineData("Title", "Comet")]
		[InlineData("inheritedCount", 17)]
		[InlineData("InheritedTitle", "Inherited")]
		public void DirectViewMembers_ReadAndWriteIncludingInheritedPrivateMembers(string name, object value)
		{
			using var view = new MemberView();

			Assert.True(view.SetDirectPropertyValue(name, value));
			Assert.Equal(value, view.GetDirectPropertyValue(name));
			Assert.Equal(value, view.GetDirectPropValue<object>(name));
			Assert.Equal(value, view.GetPropertyValue(name));
			Assert.True(view.SetDeepPropertyValue(name, value));
			Assert.True(((object)view).SetDeepPropertyValue(name, value));
			Assert.Equal(value, ((object)view).GetPropertyValue(name));
		}

		[Fact]
		public void DirectViewMembers_RejectNestedPathsWithoutMutating()
		{
			using var view = new MemberView();
			const string path = "Nested.Name";

			Assert.Throws<ArgumentException>(() => view.GetDirectPropertyValue(path));
			Assert.Throws<ArgumentException>(() => view.GetDirectPropValue<string>(path));
			Assert.Throws<ArgumentException>(() => view.SetDirectPropertyValue(path, "Ignored"));
			Assert.Null(view.Nested.Name);
		}

		[Fact]
		public void DirectViewMembers_NullAndMissingMembersKeepLegacyDefaults()
		{
			using var view = new MemberView();
			Assert.Null(view.GetDirectPropertyValue("Missing"));
			Assert.Equal(0, view.GetDirectPropValue<int>("Missing"));
			Assert.False(view.SetDirectPropertyValue("Missing", 1));
			Assert.True(view.SetDirectPropertyValue(nameof(MemberView.Title), null));
			Assert.Null(view.GetDirectPropValue<string>(nameof(MemberView.Title)));

			View nullView = null;
			Assert.Null(nullView.GetDirectPropertyValue("Missing"));
			Assert.Equal(0, nullView.GetDirectPropValue<int>("Missing"));
			Assert.False(nullView.SetDirectPropertyValue("Missing", 1));
		}

		[Fact]
		public void NestedReflectionEntryPoints_ExposeWarningsInsteadOfSuppressingThem()
		{
			var methods = typeof(ReflectionExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
				.Where(method => method.Name == nameof(ReflectionExtensions.GetPropertyValue)
					|| method.Name == nameof(ReflectionExtensions.GetPropValue)
					|| method.Name == nameof(ReflectionExtensions.SetDeepPropertyValue))
				.ToArray();

			Assert.Equal(7, methods.Length);
			Assert.All(methods, method => {
				Assert.NotNull(method.GetCustomAttribute<RequiresUnreferencedCodeAttribute>());
				Assert.DoesNotContain(method.GetCustomAttributes<UnconditionalSuppressMessageAttribute>(),
					attribute => attribute.CheckId == "IL2026");
			});

			var directMethods = typeof(ReflectionExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
				.Where(method => method.Name == nameof(ReflectionExtensions.GetDirectPropertyValue)
					|| method.Name == nameof(ReflectionExtensions.GetDirectPropValue)
					|| method.Name == nameof(ReflectionExtensions.SetDirectPropertyValue))
				.ToArray();

			Assert.Equal(3, directMethods.Length);
			Assert.All(directMethods, method => {
				Assert.Equal(typeof(View), method.GetParameters()[0].ParameterType);
				Assert.Null(method.GetCustomAttribute<RequiresUnreferencedCodeAttribute>());
			});
		}

		[Fact]
		public void LegacyObjectExtensionOverloadsRemainCallable()
		{
			object instance = new PlainObject();

			Assert.True(instance.SetPropertyValue(nameof(PlainObject.Title), "Comet"));
			Assert.Equal("Comet", instance.GetPropertyValue(nameof(PlainObject.Title)));
			Assert.Equal("Comet", instance.GetPropValue<string>(nameof(PlainObject.Title)));
			Assert.True(instance.SetDeepPropertyValue(
				$"{nameof(PlainObject.Nested)}.{nameof(NestedValue.Name)}",
				"NativeAOT"));
			Assert.Equal(
				"NativeAOT",
				instance.GetPropertyValue(
					$"{nameof(PlainObject.Nested)}.{nameof(NestedValue.Name)}"));
			Assert.Single(instance.GetFieldsWithAttribute(typeof(MarkedFieldAttribute)));
		}

		[Fact]
		public void LegacyObjectExtensionMethodsRetainTheirBinaryApiShape()
		{
			var reflectionMethods = typeof(ReflectionExtensions).GetMethods(
				BindingFlags.Public | BindingFlags.Static);

			Assert.Contains(
				reflectionMethods,
				method => method.Name == nameof(ReflectionExtensions.SetPropertyValue)
					&& method.IsGenericMethodDefinition
					&& method.GetParameters()[0].ParameterType == typeof(object));
			Assert.Contains(
				reflectionMethods,
				method => method.Name == nameof(ReflectionExtensions.SetPropertyValue)
					&& !method.IsGenericMethod
					&& method.GetParameters()[0].ParameterType == typeof(object));
			Assert.Contains(
				reflectionMethods,
				method => method.Name == nameof(ReflectionExtensions.SetDeepPropertyValue)
					&& method.GetParameters()[0].ParameterType == typeof(object));
			Assert.Contains(
				reflectionMethods,
				method => method.Name == nameof(ReflectionExtensions.GetPropertyValue)
					&& method.GetParameters()[0].ParameterType == typeof(object));
			Assert.Contains(
				reflectionMethods,
				method => method.Name == nameof(ReflectionExtensions.GetPropValue)
					&& method.IsGenericMethodDefinition
					&& method.GetParameters()[0].ParameterType == typeof(object));

			var fieldsMethod = typeof(global::Comet.ViewExtensions).GetMethods(
					BindingFlags.Public | BindingFlags.Static)
				.Single(method => method.Name == "GetFieldsWithAttribute"
					&& method.GetParameters()[0].ParameterType == typeof(object));

			Assert.Equal(typeof(Type), fieldsMethod.GetParameters()[1].ParameterType);
		}
	}
}
