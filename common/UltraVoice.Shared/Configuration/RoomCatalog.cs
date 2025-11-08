using System.Collections.Generic;

namespace UltraVoice.Shared.Configuration;

/// <summary>
/// Centralized default room catalog so both client and server stay in sync.
/// </summary>
public static class RoomCatalog
{
    private static readonly string[] DefaultRoomSet = ["Man Cave", "Genel", "Chill Cave"];

    public const string DefaultRoom = "Man Cave";

    public static IReadOnlyList<string> DefaultRooms => DefaultRoomSet;

    public static string[] CreateDefaultRooms() => (string[])DefaultRoomSet.Clone();

    public static string Normalize(string? roomId) =>
        string.IsNullOrWhiteSpace(roomId) ? DefaultRoom : roomId;
}
