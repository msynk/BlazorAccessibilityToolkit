using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BlazorAccessibilityToolkit.Cli;
using BlazorAccessibilityToolkit.ComponentTesting;
using BlazorAccessibilityToolkit.Playwright;

namespace BlazorAccessibilityToolkit.Tests;

/// <summary>
/// Snapshots the public API of every package. A difference means the public surface changed: review it for breaking
/// changes, document it in CHANGELOG.md, and update the snapshot with BLAZOR_A11Y_UPDATE_SNAPSHOTS=1.
/// </summary>
public sealed class PublicApiTests
{
    public static TheoryData<string> Packages => new()
    {
        typeof(AccessibilityAudit).Assembly.GetName().Name!,
        typeof(AccessibilityFixture).Assembly.GetName().Name!,
        typeof(PlaywrightAccessibility).Assembly.GetName().Name!,
        typeof(CliApp).Assembly.GetName().Name!,
    };

    [Theory]
    [MemberData(nameof(Packages))]
    public void Public_api_matches_the_snapshot(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);
        var actual = Describe(assembly);
        var path = Path.Combine(SnapshotDirectory(), assemblyName + ".txt");

        if (Environment.GetEnvironmentVariable("BLAZOR_A11Y_UPDATE_SNAPSHOTS") is "1" or "true")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
        }

        Assert.True(File.Exists(path), $"API snapshot '{path}' is missing; run with BLAZOR_A11Y_UPDATE_SNAPSHOTS=1.");
        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert.True(expected == actual, $"The public API of {assemblyName} changed. Review for breaking changes, then update the snapshot with BLAZOR_A11Y_UPDATE_SNAPSHOTS=1.\n{Diff(expected, actual)}");
    }

    private static string Describe(Assembly assembly)
    {
        var lines = new List<string>();
        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            lines.Add($"{Kind(type)} {Name(type)}{(type.BaseType is { } b && b != typeof(object) && b != typeof(ValueType) && b != typeof(Enum) ? " : " + Name(b) : string.Empty)}");
            if (type.IsEnum)
            {
                lines.AddRange(Enum.GetNames(type).Select(n => $"  {n}"));
                continue;
            }

            var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m is not MethodInfo { IsSpecialName: true } && m is not Type)
                .Where(m => m.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
                .Select(Member)
                .Where(m => m is not null)
                .Order(StringComparer.Ordinal);
            lines.AddRange(members.Select(m => "  " + m));
        }

        return string.Join("\n", lines) + "\n";
    }

    private static string? Member(MemberInfo member) => member switch
    {
        ConstructorInfo c => $"ctor({Parameters(c.GetParameters())})",
        MethodInfo m when m.Name is "Equals" or "GetHashCode" or "ToString" or "<Clone>$" or "Deconstruct" or "PrintMembers" => null,
        MethodInfo m => $"{(m.IsStatic ? "static " : string.Empty)}{Name(m.ReturnType)} {m.Name}{(m.IsGenericMethod ? "<" + string.Join(", ", m.GetGenericArguments().Select(Name)) + ">" : string.Empty)}({Parameters(m.GetParameters())})",
        PropertyInfo p when p.Name == "EqualityContract" => null,
        PropertyInfo p => $"{(p.GetMethod?.IsStatic == true ? "static " : string.Empty)}{Name(p.PropertyType)} {p.Name} {{ {(p.GetMethod?.IsPublic == true ? "get; " : string.Empty)}{(p.SetMethod?.IsPublic == true ? (p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Any(t => t.Name == "IsExternalInit") ? "init; " : "set; ") : string.Empty)}}}",
        FieldInfo f => $"{(f.IsLiteral ? "const " : f.IsStatic ? "static " : string.Empty)}{Name(f.FieldType)} {f.Name}",
        EventInfo e => $"event {Name(e.EventHandlerType!)} {e.Name}",
        _ => null,
    };

    private static string Parameters(ParameterInfo[] parameters) =>
        string.Join(", ", parameters.Select(p => $"{Name(p.ParameterType)} {p.Name}{(p.HasDefaultValue ? " = " + (p.DefaultValue?.ToString() ?? "null") : string.Empty)}"));

    private static string Kind(Type type) =>
        type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct" : type.IsAbstract && type.IsSealed ? "static class" : type.IsAbstract ? "abstract class" : type.IsSealed ? "sealed class" : "class";

    private static string Name(Type type)
    {
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type.IsArray)
        {
            return Name(type.GetElementType()!) + "[]";
        }

        if (type.IsByRef)
        {
            return "ref " + Name(type.GetElementType()!);
        }

        var name = type.IsNested ? Name(type.DeclaringType!) + "." + type.Name : type.Namespace + "." + type.Name;
        if (!type.IsGenericType)
        {
            return name;
        }

        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return (tick >= 0 ? name[..tick] : name) + "<" + string.Join(", ", type.GetGenericArguments().Select(Name)) + ">";
    }

    private static string Diff(string expected, string actual)
    {
        var e = expected.Split('\n').ToHashSet();
        var a = actual.Split('\n').ToHashSet();
        var sb = new StringBuilder();
        foreach (var line in expected.Split('\n').Where(l => !a.Contains(l)))
        {
            sb.Append("- ").AppendLine(line);
        }

        foreach (var line in actual.Split('\n').Where(l => !e.Contains(l)))
        {
            sb.Append("+ ").AppendLine(line);
        }

        return sb.ToString();
    }

    private static string SnapshotDirectory([CallerFilePath] string source = "") => Path.Combine(Path.GetDirectoryName(source)!, "PublicApi");
}
