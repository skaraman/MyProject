using System;
using System.Collections.Generic;
using UnityEngine;

public static class ButtonShaderKeywords {
  const string BackChildName = "back";
  const string TextChildPrefix = "text";

  static readonly List<AllIn1AnimatorInspector> resolvedAnimators = new(8);
  static readonly HashSet<ulong> resolvedAnimatorIds = new();
  static readonly HashSet<ulong> loggedMissingKeywordDeclarations = new();

  public static int ApplyToButton(GameObject button, string keyword, bool enabled) {
    if (button == null || string.IsNullOrWhiteSpace(keyword)) {
      return 0;
    }

    ResolveButtonAnimators(button, resolvedAnimators);
    var validateSerializedKeywords = HasAncestorNamed(button.transform, "PauseMenu");
    var appliedCount = 0;

    for (var i = 0; i < resolvedAnimators.Count; i++) {
      appliedCount += ApplyToAnimator(button, resolvedAnimators[i], keyword, enabled, validateSerializedKeywords) ? 1 : 0;
    }

    resolvedAnimators.Clear();
    resolvedAnimatorIds.Clear();
    return appliedCount;
  }

  public static bool ApplyToAnimator(GameObject button, AllIn1AnimatorInspector animator, string keyword, bool enabled) {
    if (button == null || string.IsNullOrWhiteSpace(keyword)) {
      return false;
    }

    return ApplyToAnimator(button, animator, keyword, enabled, HasAncestorNamed(button.transform, "PauseMenu"));
  }

  static bool ApplyToAnimator(GameObject button, AllIn1AnimatorInspector animator, string keyword, bool enabled, bool validateSerializedKeywords) {
    if (animator == null) {
      return false;
    }

    if (validateSerializedKeywords) {
      ValidateSerializedKeyword(button, animator, keyword);
    }

    animator.SetKeyword(keyword, enabled);
    return true;
  }

  static void ResolveButtonAnimators(GameObject button, List<AllIn1AnimatorInspector> animators) {
    animators.Clear();
    resolvedAnimatorIds.Clear();

    AddAnimator(button.GetComponent<AllIn1AnimatorInspector>(), animators);
    AddShaderReferenceAnimators(button.GetComponent<ReferenceListAllIn1AnimatorInspector>(), animators);
    AddVisualReferenceAnimators(button.GetComponent<ReferenceListGameObject>(), animators);

    if (animators.Count > 0) {
      return;
    }

    AddNamedChildAnimator(button.transform, BackChildName, animators);
    AddImmediateChildAnimators(button.transform, animators);
  }

  static void AddShaderReferenceAnimators(ReferenceListAllIn1AnimatorInspector referenceList, List<AllIn1AnimatorInspector> animators) {
    if (referenceList == null || referenceList.references == null) {
      return;
    }

    for (var i = 0; i < referenceList.references.Count; i++) {
      AddAnimator(referenceList.references[i], animators);
    }
  }

  static void AddVisualReferenceAnimators(ReferenceListGameObject referenceList, List<AllIn1AnimatorInspector> animators) {
    if (referenceList == null || referenceList.references == null) {
      return;
    }

    for (var i = 0; i < referenceList.references.Count; i++) {
      AddVisualAnimatorHierarchy(referenceList.references[i], animators);
    }
  }

  static void AddVisualAnimatorHierarchy(GameObject visualRoot, List<AllIn1AnimatorInspector> animators) {
    if (visualRoot == null) {
      return;
    }

    AddAnimator(visualRoot.GetComponent<AllIn1AnimatorInspector>(), animators);
    AddNamedChildAnimator(visualRoot.transform, BackChildName, animators);
  }

  static void AddNamedChildAnimator(Transform root, string childName, List<AllIn1AnimatorInspector> animators) {
    if (root == null) {
      return;
    }

    var child = root.Find(childName);
    if (child == null) {
      return;
    }

    AddAnimator(child.GetComponent<AllIn1AnimatorInspector>(), animators);
  }

  static void AddImmediateChildAnimators(Transform root, List<AllIn1AnimatorInspector> animators) {
    if (root == null) {
      return;
    }

    for (var i = 0; i < root.childCount; i++) {
      var child = root.GetChild(i);
      if (IsTextTransform(child)) {
        continue;
      }

      AddAnimator(child.GetComponent<AllIn1AnimatorInspector>(), animators);
    }
  }

  static void AddAnimator(AllIn1AnimatorInspector animator, List<AllIn1AnimatorInspector> animators) {
    if (animator == null) {
      return;
    }

    var animatorId = ObjectEntityId.GetRawValue(animator);
    if (!resolvedAnimatorIds.Add(animatorId)) {
      return;
    }

    animators.Add(animator);
  }

  static void ValidateSerializedKeyword(GameObject button, AllIn1AnimatorInspector animator, string keyword) {
    if (HasSerializedKeyword(animator, keyword)) {
      return;
    }

    var warningKey = (ObjectEntityId.GetRawValue(animator) * 397UL) ^ unchecked((ulong)(uint)keyword.GetHashCode());
    if (!loggedMissingKeywordDeclarations.Add(warningKey)) {
      return;
    }

    Debug.LogWarning(
      "[ButtonShaderKeywords] Missing serialized keyword='" + keyword +
      "' button='" + button.name +
      "' animator='" + animator.gameObject.name +
      "' path='" + GetHierarchyPath(animator.transform) + "'"
    );
  }

  static bool HasSerializedKeyword(AllIn1AnimatorInspector animator, string keyword) {
    if (animator == null || animator.keywordToggles == null) {
      return false;
    }

    for (var i = 0; i < animator.keywordToggles.Count; i++) {
      var toggle = animator.keywordToggles[i];
      if (toggle != null && toggle.keyword == keyword) {
        return true;
      }
    }

    return false;
  }

  static bool HasAncestorNamed(Transform target, string ancestorName) {
    for (var current = target; current != null; current = current.parent) {
      if (current.name == ancestorName) {
        return true;
      }
    }

    return false;
  }

  static bool IsTextTransform(Transform target) {
    if (target == null) {
      return false;
    }

    if (target.name.StartsWith(TextChildPrefix, StringComparison.OrdinalIgnoreCase)) {
      return true;
    }

    return target.GetComponent<FontText>() != null;
  }

  static string GetHierarchyPath(Transform target) {
    if (target == null) {
      return string.Empty;
    }

    var path = target.name;
    for (var current = target.parent; current != null; current = current.parent) {
      path = current.name + "/" + path;
    }

    return path;
  }
}
