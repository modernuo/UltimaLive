# UltimaLive for ModernUO

Based on https://github.com/praxiiz/UltimaLive

## Installation

1. Copy files somewhere into UOContent
2. Setup your shard identifier in UltimaLiveSettings.cs
3. Make core modifications
    - Make your **PlayerMobile** class **partial**
    - add this to 
        - **bottom** of method PlayerMobile.**SetLocation**(Point3D loc, bool isTeleport)
        - **top** of method PlayerMobile.**OnMapChange**(Map oldMap)
    ```csharp
    /* Begin UltimaLive Mod */
    if (BlockQuery != null)
    {
        m_PreviousMapBlock = BlockQuery.QueryMobile(this, m_PreviousMapBlock);
    }
    /* End UltimaLive Mod */
    ```
    - **TileMatrix.cs** doesn't have to be changed unlike original