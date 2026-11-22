namespace NajmCard.Core.Services;

/// <summary>RouterOS boolean values are three-state: absent or invalid is unknown.</summary>
public static class RouterOsBoolean
{
    public static bool? Parse(string? value)=>value?.Trim().ToLowerInvariant() switch
    {
        "yes" or "true" or "1"=>true,
        "no" or "false" or "0"=>false,
        _=>null
    };

    public static bool TryParse(string? value,out bool result)
    {
        var parsed=Parse(value);result=parsed==true;return parsed.HasValue;
    }
}
