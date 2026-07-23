using UnityEngine;

/// <summary>
/// Allows for two other gamenodes to be registered as attached pieces for physics considerations later on.
/// </summary>
public class ConnectedPieces : MonoBehaviour
{
    [Tooltip("The first attached piece")]
    public GameObject attachedPiece1;

    [Tooltip("The second attached piece")]
    public GameObject attachedPiece2;

    [Tooltip("Paired node (e.g., opposite side or linked node)")]
    public GameObject pair;
}
