/// <summary>A cutting board or plate that a paper towel can be spread on first,
/// for meat/tofu set on it to drain (IngredientItem's drainOnTowel).</summary>
public interface ITowelLined
{
    bool HasTowelLiner { get; }

    /// <summary>A towel brought here already spread (folded round a piece and
    /// carried over) becomes this board/plate's liner.</summary>
    void AdoptLiner(ToolItem towel);
}
