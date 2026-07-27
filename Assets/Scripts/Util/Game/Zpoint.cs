using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

public class Zpoint : MonoBehaviour {
  private SortingGroup sortingGroup;
  private Renderer targetRenderer;
  private Transform cachedTransform;
  private bool searchedSortingGroup;
  private Vector3 lastPosition = new(float.MinValue, float.MinValue, float.MinValue);

  private const float Y_MOVEMENT_THRESHOLD = 0.01f;
  private const float Y_TO_SORTING_ORDER_MULTIPLIER = 100f;
  private const int SORT_UPDATE_INTERVAL_FRAMES = 2;

  static readonly List<Zpoint>[] s_ActiveByFrameBucket = {
    new List<Zpoint>(),
    new List<Zpoint>()
  };
  static int s_NextSortFrameBucket;
  int _activeListIndex = -1;
  int _sortFrameBucket;
  static bool s_UpdateRegistered;
  static readonly System.Action s_UpdateCallback = UpdateAll;

  static void UpdateAll() {
    var active = s_ActiveByFrameBucket[Time.frameCount % SORT_UPDATE_INTERVAL_FRAMES];
    var remaining = active.Count;
    var index = 0;
    while (index < active.Count && remaining-- > 0) {
      var target = active[index];
      target.ManagedUpdate();
      if (index < active.Count && active[index] == target) {
        index++;
      }
    }
  }

  static void EnsureUpdateRegistration() {
    if (s_UpdateRegistered || !Application.isPlaying) return;
    s_UpdateRegistered = true;
    RuntimeUpdateHub.Register(
      200,
      "RuntimeUpdateHub.Zpoint",
      s_UpdateCallback
    );
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetStatics() {
    for (var bucket = 0; bucket < s_ActiveByFrameBucket.Length; bucket++) {
      var active = s_ActiveByFrameBucket[bucket];
      for (var i = 0; i < active.Count; i++) {
        var target = active[i];
        if (target != null) {
          target._activeListIndex = -1;
        }
      }
      active.Clear();
    }
    s_NextSortFrameBucket = 0;
    s_UpdateRegistered = false;
  }

  void Awake() {
    cachedTransform = transform;
  }

  void OnEnable() {
    if (Application.isPlaying && _activeListIndex < 0) {
      _sortFrameBucket = s_NextSortFrameBucket;
      s_NextSortFrameBucket =
        (s_NextSortFrameBucket + 1) % SORT_UPDATE_INTERVAL_FRAMES;
      var active = s_ActiveByFrameBucket[_sortFrameBucket];
      _activeListIndex = active.Count;
      active.Add(this);
      EnsureUpdateRegistration();
      lastPosition = new Vector3(float.MinValue, float.MinValue, float.MinValue);
      ManagedUpdate();
    }
  }

  void OnDisable() {
    if (Application.isPlaying && _activeListIndex >= 0) {
      var active = s_ActiveByFrameBucket[_sortFrameBucket];
      var lastIndex = active.Count - 1;
      var removeIndex =
        _activeListIndex <= lastIndex && active[_activeListIndex] == this
          ? _activeListIndex
          : active.IndexOf(this);
      if (removeIndex >= 0) {
        var last = active[lastIndex];
        active[removeIndex] = last;
        if (last != null) {
          last._activeListIndex = removeIndex;
        }
        active.RemoveAt(lastIndex);
      }
      _activeListIndex = -1;
    }
  }

  void Start() {
    if (sortingGroup == null && targetRenderer == null) {
      searchedSortingGroup = false;
    }
    ResolveReferences();
  }

  void OnTransformParentChanged() {
    searchedSortingGroup = false;
    sortingGroup = null;
    targetRenderer = null;
  }

  void ResolveReferences() {
    if (!searchedSortingGroup && sortingGroup == null && targetRenderer == null) {
      searchedSortingGroup = true;
      TryGetComponent(out sortingGroup);
      if (sortingGroup == null && cachedTransform.parent != null) {
        cachedTransform.parent.TryGetComponent(out sortingGroup);
      }
      if (sortingGroup == null) {
        TryGetComponent(out targetRenderer);
        if (targetRenderer == null && cachedTransform.parent != null) {
          cachedTransform.parent.TryGetComponent(out targetRenderer);
        }
      }
    }
  }

  internal void ManagedUpdate() {
    ResolveReferences();
    if (sortingGroup == null && targetRenderer == null) return;

    Vector3 pos = cachedTransform.position;
    if (Mathf.Abs(pos.y - lastPosition.y) < Y_MOVEMENT_THRESHOLD && lastPosition.x != float.MinValue) {
      return;
    }
    lastPosition = pos;

    int newOrder = -(int)(pos.y * Y_TO_SORTING_ORDER_MULTIPLIER);

    if (sortingGroup != null) {
      if (sortingGroup.sortingOrder != newOrder) {
        sortingGroup.sortingOrder = newOrder;
      }
    } else if (targetRenderer != null) {
      if (targetRenderer.sortingOrder != newOrder) {
        targetRenderer.sortingOrder = newOrder;
      }
    }
  }
}
