// Phase 2.3: Command Skill registry.
// Registry pattern for handler discovery, consistent with MovementHandlerRegistry.

namespace HeroChess.Rules.Skills;

/// <summary>
/// Registry for Command Skill handlers.
/// Resolves an ImplementationKey to an ICommandSkillHandler.
///
/// Registered handlers are matched by their ImplementationKey property.
/// The registry is built at startup; handlers are resolved by key lookup (O(1)).
/// Unregistered keys return false/null.
///
/// Design rationale:
/// - Consistent with the existing MovementHandlerRegistry pattern in MovementHandlers.cs.
/// - Keyed by ImplementationKey (the stable identifier from FrozenSkill).
/// - Supports future addition of new Command Skills without modifying dispatcher code.
/// - Thread-safe for reads (immutable after construction, no locking needed at runtime).
/// </summary>
public sealed class CommandSkillRegistry
{
    private readonly IReadOnlyDictionary<string, ICommandSkillHandler> _handlers;

    /// <summary>
    /// Creates an empty registry. Populate via With() fluent method or constructor.
    /// </summary>
    public CommandSkillRegistry(IEnumerable<ICommandSkillHandler>? handlers = null)
    {
        var dict = new Dictionary<string, ICommandSkillHandler>(StringComparer.Ordinal);
        if (handlers != null)
            foreach (var h in handlers)
                dict[h.ImplementationKey] = h;
        _handlers = dict;
    }

    // Private constructor for With() to pass a pre-built dictionary.
    private CommandSkillRegistry(IReadOnlyDictionary<string, ICommandSkillHandler> handlers) =>
        _handlers = handlers;

    /// <summary>
    /// Returns a new registry with the given handler added (fluent API).
    /// The original registry is unchanged.
    /// </summary>
    public CommandSkillRegistry With(ICommandSkillHandler handler)
    {
        var dict = new Dictionary<string, ICommandSkillHandler>(_handlers, StringComparer.Ordinal);
        dict[handler.ImplementationKey] = handler;
        return new CommandSkillRegistry(dict);
    }

    /// <summary>
    /// Attempts to resolve a handler for the given ImplementationKey.
    /// </summary>
    /// <param name="key">The FrozenSkill.ImplementationKey to resolve.</param>
    /// <param name="handler">The handler if found.</param>
    /// <returns>True if a registered handler exists for this key.</returns>
    public bool TryGet(string? key, out ICommandSkillHandler? handler)
    {
        if (key != null && _handlers.TryGetValue(key, out var found))
        {
            handler = found;
            return true;
        }
        handler = null;
        return false;
    }

    /// <summary>
    /// Returns true if a handler is registered for the given key.
    /// </summary>
    public bool IsRegistered(string? key) =>
        key != null && _handlers.ContainsKey(key);

    /// <summary>
    /// Returns all registered implementation keys (for diagnostics/testing).
    /// </summary>
    public IReadOnlyCollection<string> RegisteredKeys => _handlers.Keys.ToList().AsReadOnly();
}
