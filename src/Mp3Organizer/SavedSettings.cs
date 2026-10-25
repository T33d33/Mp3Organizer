namespace Mp3Organizer;

public static class SavedSettings
{
    // A terminal opened before setup may not have inherited persisted settings yet.
    public static string? Read(string name) => Resolve(name, Environment.GetEnvironmentVariable, OperatingSystem.IsWindows());

    public static string? Resolve(string name, Func<string,EnvironmentVariableTarget,string?> read, bool windows)
    {
        var value=read(name,EnvironmentVariableTarget.Process);
        if(!string.IsNullOrWhiteSpace(value))return value;
        if(!windows)return null;
        value=read(name,EnvironmentVariableTarget.User);
        return !string.IsNullOrWhiteSpace(value)?value:read(name,EnvironmentVariableTarget.Machine);
    }
}
