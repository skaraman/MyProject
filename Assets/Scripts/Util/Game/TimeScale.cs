using System;
using System.Collections;
using System.Collections.Generic;

public static class TimeScale {
  public static Dictionary<int, float> Factors { set; get; } = new Dictionary<int, float> { { 1, 1f }, { 2, 1f }, { 3, 1f }, { 4, 1f } };
}