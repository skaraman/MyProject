using System;
using UnityEngine;

/// <summary>
/// UI View component that formats and displays the current Day/Night cycle time
/// onto FontText components (similar to GameplayZoneInfoView).
/// </summary>
public sealed class GameplayGameTimeView : MonoBehaviour {
  [Header("FontText UI Targets")]
  [SerializeField] FontText timeText;
  [SerializeField] FontText phaseText;
  [SerializeField] FontText dayText;

  [Header("Display Format")]
  [SerializeField] bool use24HourFormat = true;
  [SerializeField] bool showPhaseText = true;
  [SerializeField] bool showDayCount = false;

  string displayedTime = "";
  string displayedPhase = "";
  string displayedDay = "";

  void OnEnable() {
    Refresh();
  }

  void Update() {
    Refresh();
  }

  public void Refresh() {
    var cycle = DayNightCycle2D.Instance;
    if (cycle == null) {
      cycle = FindAnyObjectByType<DayNightCycle2D>();
    }
    if (cycle == null) {
      return;
    }

    var timeStr = cycle.GetFormattedTime(use24HourFormat);
    var phaseStr = showPhaseText ? cycle.GetPhaseName() : "";
    var dayStr = showDayCount ? $"DAY {cycle.DayCount}" : "";

    if (!string.Equals(displayedTime, timeStr, StringComparison.Ordinal)) {
      displayedTime = timeStr;
      ApplyText(timeText, displayedTime);
    }

    if (!string.Equals(displayedPhase, phaseStr, StringComparison.Ordinal)) {
      displayedPhase = phaseStr;
      ApplyText(phaseText, displayedPhase);
    }

    if (!string.Equals(displayedDay, dayStr, StringComparison.Ordinal)) {
      displayedDay = dayStr;
      ApplyText(dayText, displayedDay);
    }
  }

  static void ApplyText(FontText text, string value) {
    if (text == null) return;
    if (string.Equals(text.content, value, StringComparison.Ordinal)) return;

    text.content = value;
    text.Generate();
  }
}
