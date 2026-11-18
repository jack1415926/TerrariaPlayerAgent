using System;

namespace TerrariaPlayerAgent.Common;

// Server-side opt-in for isolated test fixtures. The client cannot set this by sending a request.
internal static class MultiplayerTestEquipPolicy
{
    internal const string EnvironmentVariable = "TPA_MP_TEST_EQUIP";
    internal const string DisabledMessage =
        "Test equipment is disabled on the server. Set TPA_MP_TEST_EQUIP=1 on the server process and restart.";

    internal static bool Enabled => Environment.GetEnvironmentVariable(EnvironmentVariable) == "1";
}
