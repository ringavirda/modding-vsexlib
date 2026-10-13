// Brings exlib's LegacyApi extension members and the FastVec3f alias into scope on legacy target frameworks.
#if !GAME_GE_1_22
global using ExpandedLib.Legacy;
#endif
#if !GAME_GE_1_21
global using FastVec3f = Vintagestory.API.MathTools.Vec3f;
#endif
