using System.Reflection;
using System.Text;
using System.Globalization;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class TestingPublicApiBaselineTests
{
    private const string UpdateEnvironmentVariable = "UPDATE_DEVFLOW_TESTING_PUBLIC_API_BASELINE";

    [Fact]
    public void PublicApi_MatchesCommittedBaseline()
    {
        var baselinePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "DevFlow",
            "Microsoft.Maui.DevFlow.Testing",
            "PublicApiBaseline.txt");
        var actual = CreatePublicApiBaseline(typeof(MauiFlow).Assembly);

        if (string.Equals(Environment.GetEnvironmentVariable(UpdateEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            Assert.False(
                IsContinuousIntegration(),
                $"{UpdateEnvironmentVariable}=1 must not update the public API baseline in CI.");
            File.WriteAllText(baselinePath, actual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        Assert.True(
            File.Exists(baselinePath),
            $"Missing public API baseline '{baselinePath}'. Set {UpdateEnvironmentVariable}=1 only when intentionally updating the preview API baseline.");
        Assert.Equal(Normalize(File.ReadAllText(baselinePath)), Normalize(actual));
    }

    private static bool IsContinuousIntegration()
        => string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("TF_BUILD"), "true", StringComparison.OrdinalIgnoreCase);

    private static string CreatePublicApiBaseline(Assembly assembly)
    {
        var entries = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var type in assembly.GetExportedTypes().OrderBy(GetTypeName, StringComparer.Ordinal))
        {
            entries.Add($"T:{GetTypeKind(type)}{GetTypeModifiers(type)} {GetTypeName(type)}");

            const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var constructor in type.GetConstructors(flags).Where(IsPublicOrProtected))
                entries.Add($"C:{GetTypeName(type)}({GetParameters(constructor.GetParameters())})");

            foreach (var method in type.GetMethods(flags).Where(method => !method.IsSpecialName && IsPublicOrProtected(method)))
            {
                entries.Add(
                    $"M:{GetTypeName(type)}.{method.Name}{GetGenericArity(method)}({GetParameters(method.GetParameters())}):{GetTypeName(method.ReturnType)}");
            }

            foreach (var property in type.GetProperties(flags).Where(IsPublicOrProtected))
            {
                entries.Add(
                    $"P:{GetTypeName(type)}.{property.Name}[{GetParameters(property.GetIndexParameters())}]:{GetTypeName(property.PropertyType)}{{{GetPropertyAccessors(property)}}}");
            }

            foreach (var field in type.GetFields(flags).Where(field => IsPublicOrProtected(field)))
                entries.Add($"F:{GetTypeName(type)}.{field.Name}:{GetTypeName(field.FieldType)}");

            foreach (var @event in type.GetEvents(flags).Where(IsPublicOrProtected))
                entries.Add($"E:{GetTypeName(type)}.{@event.Name}:{GetTypeName(@event.EventHandlerType!)}");
        }

        return string.Join('\n', entries) + '\n';
    }

    private static bool IsPublicOrProtected(ConstructorInfo constructor)
        => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly;

    private static bool IsPublicOrProtected(MethodBase method)
        => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static bool IsPublicOrProtected(PropertyInfo property)
        => (property.GetMethod is not null && IsPublicOrProtected(property.GetMethod)) ||
            (property.SetMethod is not null && IsPublicOrProtected(property.SetMethod));

    private static bool IsPublicOrProtected(FieldInfo field)
        => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;

    private static bool IsPublicOrProtected(EventInfo @event)
        => (@event.AddMethod is not null && IsPublicOrProtected(@event.AddMethod)) ||
            (@event.RemoveMethod is not null && IsPublicOrProtected(@event.RemoveMethod));

    private static string GetParameters(IEnumerable<ParameterInfo> parameters)
        => string.Join(",", parameters.Select(parameter =>
            $"{parameter.Name}:{GetTypeName(parameter.ParameterType)}{GetDefaultValue(parameter)}"));

    private static string GetDefaultValue(ParameterInfo parameter)
    {
        if (!parameter.HasDefaultValue)
            return string.Empty;

        return "=" + (parameter.RawDefaultValue switch
        {
            null => "null",
            DBNull => "<dbnull>",
            Missing => "<missing>",
            string value => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
            char value => $"'{value}'",
            bool value => value ? "true" : "false",
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
            object value => value.ToString() ?? "<null-string>",
        });
    }

    private static string GetGenericArity(MethodInfo method)
        => method.IsGenericMethodDefinition ? $"``{method.GetGenericArguments().Length}" : string.Empty;

    private static string GetTypeKind(Type type)
    {
        if (type.IsInterface)
            return "interface";
        if (type.IsEnum)
            return "enum";
        if (typeof(MulticastDelegate).IsAssignableFrom(type.BaseType))
            return "delegate";
        if (type.IsValueType)
            return "struct";

        return "class";
    }

    private static string GetTypeModifiers(Type type)
    {
        if (type.IsAbstract && type.IsSealed)
            return " static";
        if (type.IsAbstract)
            return " abstract";
        if (type.IsSealed)
            return " sealed";

        return string.Empty;
    }

    private static string GetPropertyAccessors(PropertyInfo property)
    {
        var accessors = new List<string>();
        if (property.GetMethod is { } getter && IsPublicOrProtected(getter))
            accessors.Add($"get:{GetAccessibility(getter)}");
        if (property.SetMethod is { } setter && IsPublicOrProtected(setter))
            accessors.Add($"set:{GetAccessibility(setter)}");

        return string.Join(";", accessors);
    }

    private static string GetAccessibility(MethodBase method)
    {
        if (method.IsPublic)
            return "public";
        if (method.IsFamilyOrAssembly)
            return "protected-internal";
        if (method.IsFamily)
            return "protected";

        return "unknown";
    }

    private static string GetTypeName(Type type)
    {
        if (type.IsByRef)
            return $"{GetTypeName(type.GetElementType()!)}&";
        if (type.IsPointer)
            return $"{GetTypeName(type.GetElementType()!)}*";
        if (type.IsArray)
            return $"{GetTypeName(type.GetElementType()!)}[{new string(',', type.GetArrayRank() - 1)}]";
        if (type.IsGenericParameter)
            return type.DeclaringMethod is null ? $"!{type.GenericParameterPosition}" : $"!!{type.GenericParameterPosition}";

        if (!type.IsGenericType)
            return type.FullName ?? type.Name;

        var genericDefinition = type.GetGenericTypeDefinition();
        var genericName = genericDefinition.FullName ?? genericDefinition.Name;
        return $"{genericName}[{string.Join(",", type.GetGenericArguments().Select(GetTypeName))}]";
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root for the public API baseline.");
    }

    private static string Normalize(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
}
