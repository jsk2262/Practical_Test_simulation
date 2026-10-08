using UnityEngine;

/// <summary>
/// A pot/pan lid - pickable and removable on its own, and placeable back onto
/// any vessel (see MovableBowl.CanAcceptDrop/PlaceLid). Kept generic rather than
/// wired to one specific pot, since more dishes (and their own lidded pots/pans)
/// will need the same piece later.
/// </summary>
public class PotLid : Carryable
{
}
