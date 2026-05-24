namespace AIExtensions.Sample.DIParameters;

/// <summary>
/// A keyed service. Pulled from DI using <c>[FromKeyedServices("premium")]</c>
/// or <c>[FromKeyedServices("free")]</c>.
/// </summary>
public interface IModelProvider
{
    string Name { get; }
}

public sealed class FreeModelProvider : IModelProvider
{
    public string Name => "free-v1";
}

public sealed class PremiumModelProvider : IModelProvider
{
    public string Name => "premium-v2";
}
