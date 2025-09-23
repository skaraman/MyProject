
using UnityEngine;

public class ItemButtons : ButtonGroup {

  public GameObject itemPrefab;
  public GameObject itemsParent;
  public GameObject itemViewer;

  protected override void HandleActiveState(GameObject button) {
    // Swap sprite to "active" variant
    button.GetComponent<ReferenceListGameObject>().Get(0).SetActive(true);
    button.GetComponent<ReferenceListGameObject>().Get(1).SetActive(false);
  }

  protected override void HandleInactiveState(GameObject button) {
    // Swap sprite to "idle" variant
    button.GetComponent<ReferenceListGameObject>().Get(0).SetActive(false);
    button.GetComponent<ReferenceListGameObject>().Get(1).SetActive(true);
  }
}
