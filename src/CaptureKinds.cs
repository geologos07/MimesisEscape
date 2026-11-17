namespace MimesisEscape;

// IDs and direction are from this build's MonsterData, MonsterSkillSequenceData and GrabData.
internal static class CaptureKinds
{
    internal const int Mimesis = 30001, MamaMonkey = 50001, QuokkaGlider = 60001;
    internal static bool Supported(int grab) => grab == Mimesis || grab == MamaMonkey || grab == QuokkaGlider;
    internal static bool Reverse(int grab) => grab == QuokkaGlider;
    internal static bool Matches(int grab, bool reverse, int monsterMasterId, bool isMimic) =>
        grab == Mimesis ? !reverse && isMimic :
        grab == MamaMonkey ? !reverse && monsterMasterId == 20000005 :
        grab == QuokkaGlider && reverse && monsterMasterId == 20000007;
}
