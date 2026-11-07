namespace BlazorAccessibilityToolkit.ComponentTesting.Tests.Components;

public sealed class GreetingService(string name)
{
    public string Greet() => $"Hello {name}";
}
