namespace Heft
{
    /// <summary>
    /// Something players can hold. Lets other code (a delivery zone that should only take an item once it is let go)
    /// ask without knowing the component: <c>go.TryGetComponent(out IHoldable h) &amp;&amp; h.IsHeld</c>.
    /// </summary>
    public interface IHoldable
    {
        /// <summary>True while at least one player holds it. Synced: the same on the server and every client.</summary>
        bool IsHeld { get; }
    }
}
