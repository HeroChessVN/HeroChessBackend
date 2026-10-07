// Phase 2.4: Shared test helpers for Command Skill handler tests.
using System.Reflection;

namespace HeroChess.Rules.Tests;

internal static class CommandSkillTestHelpers
{
    /// <summary>Checks whether an event object has a property named "type" with the given value.</summary>
    internal static bool EventHasType(object? e, string typeName) =>
        e != null && e.GetType().GetProperty("type")?.GetValue(e)?.ToString() == typeName;

    /// <summary>Checks whether an event object has a boolean property with the given value.</summary>
    internal static bool EventHasBoolProperty(object? e, string propertyName, bool expectedValue)
    {
        if (e == null) return false;
        var prop = e.GetType().GetProperty(propertyName);
        return prop != null && prop.PropertyType == typeof(bool) && (bool)prop.GetValue(e)! == expectedValue;
    }
}
