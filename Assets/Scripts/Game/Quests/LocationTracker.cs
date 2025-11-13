public static class LocationTracker {
  public static string currentLocation = "nowhere";

  public static void UpdateLocation(string newLocation) {
    currentLocation = newLocation;
    MessageBus.Send("LocationUpdated", newLocation);
  }


}