using UnityEngine;
using System.Collections.Generic;

public class Destructible : MonoBehaviour
{
    [Header("Colliders")]
    public BoxCollider2D leftCollider;
    public BoxCollider2D rightCollider;

    [Header("Connected Nodes")]
    public List<ConnectedPieces> leftNodes = new List<ConnectedPieces>();
    public List<ConnectedPieces> rightNodes = new List<ConnectedPieces>();

    [Header("Launch Settings")]
    public float minLaunchForce = 5f;
    public float maxLaunchForce = 15f;
    public int minDepth = 1;
    public int maxDepth = 3;

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // Check which collider was hit
        if (collision.otherCollider == leftCollider)
        {
            HandleCollision(leftNodes);
        }
        else if (collision.otherCollider == rightCollider)
        {
            HandleCollision(rightNodes);
        }
    }

    private void HandleCollision(List<ConnectedPieces> nodesList)
    {
        if (nodesList == null || nodesList.Count == 0) return;

        // Determine a random depth to destruct
        int depth = Random.Range(minDepth, maxDepth + 1);
        int nodesToRemove = Mathf.Min(depth, nodesList.Count);

        for (int i = 0; i < nodesToRemove; i++)
        {
            // We always take the first node (index 0) since we remove it,
            // the rest of the list will automatically shift down.
            ConnectedPieces node = nodesList[0];
            nodesList.RemoveAt(0);

            if (node != null)
            {
                // Launch the main node
                LaunchPiece(node.gameObject);
                
                // Launch its attached pieces defined in ConnectedPieces
                if (node.attachedPiece1 != null) LaunchPiece(node.attachedPiece1);
                if (node.attachedPiece2 != null) LaunchPiece(node.attachedPiece2);

                // Disable the paired node if it exists
                if (node.pair != null)
                {
                    node.pair.SetActive(false);
                }
            }
        }
    }

    private void LaunchPiece(GameObject pieceObj)
    {
        if (pieceObj == null) return;

        // Get or add Rigidbody2D to apply physics forces
        Rigidbody2D rb = pieceObj.GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = pieceObj.AddComponent<Rigidbody2D>();
        }

        // Make sure it can move
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.simulated = true;
        
        // Calculate a random upward/outward force
        Vector2 forceDirection = new Vector2(Random.Range(-1f, 1f), Random.Range(0.5f, 1.5f)).normalized;
        float forceMagnitude = Random.Range(minLaunchForce, maxLaunchForce);
        
        rb.AddForce(forceDirection * forceMagnitude, ForceMode2D.Impulse);
        
        // Add random torque for a tumbling effect
        rb.AddTorque(Random.Range(-10f, 10f), ForceMode2D.Impulse);
    }
}
