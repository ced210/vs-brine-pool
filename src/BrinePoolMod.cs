using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VsBrinePool
{
    /// <summary>
    /// Main mod system for the Brine Pool mod.
    ///
    /// Brine pools are shallow, flat depressions filled with highly saline water.
    /// The landform shape is defined in:
    ///   assets/vsbrinepool/patches/game/worldgen/landforms.json
    ///
    /// This mod system hooks into world generation to ensure the brine pool
    /// depression is filled with still water blocks after terrain is generated.
    /// </summary>
    public class BrinePoolMod : ModSystem
    {
        private ICoreServerAPI? sapi;
        private IBulkBlockAccessor? worldGenBlockAccessor;
        private int waterBlockId;

        /// <summary>Only relevant on the server side.</summary>
        public override bool ShouldLoad(EnumAppSide forSide) =>
            forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            // Initialise the worldgen block accessor once the world generator is ready.
            api.Event.InitWorldGenerator(InitWorldGenerator, "standard");

            // Hook into the Decorations worldgen pass so we can place water blocks
            // inside brine pool depressions after terrain has been shaped.
            api.Event.RegisterChunkColumnGeneration(
                OnChunkColumnGeneration,
                EnumWorldGenPass.Decorations,
                "standard");
        }

        private void InitWorldGenerator()
        {
            if (sapi == null) return;

            // Create a reusable bulk block accessor for efficient block placement.
            worldGenBlockAccessor = sapi.WorldManager.GetBlockAccessorBulkUpdate(true, true);

            // Cache the still-water block ID once to avoid per-column lookups.
            Block waterBlock = sapi.World.GetBlock(new AssetLocation("game:water-still-7"));
            waterBlockId = waterBlock?.BlockId ?? 0;
        }

        /// <summary>
        /// Called for every newly generated chunk column.
        /// Scans the column for surface positions that sit near sea level
        /// (indicating the brine pool depression) and fills them with still water.
        /// </summary>
        private void OnChunkColumnGeneration(
            IServerChunk[] chunks,
            int chunkX,
            int chunkZ,
            ITreeAttribute chunkGenParams)
        {
            if (sapi == null || worldGenBlockAccessor == null || waterBlockId == 0) return;

            int chunkSize = sapi.WorldManager.ChunkSize;
            // Use the engine-provided sea level so this works correctly regardless
            // of the world height setting chosen at world creation.
            int seaLevel = sapi.World.SeaLevel;

            IMapChunk mapChunk = chunks[0].MapChunk;

            for (int localX = 0; localX < chunkSize; localX++)
            {
                for (int localZ = 0; localZ < chunkSize; localZ++)
                {
                    // WorldGenTerrainHeightMap stores the surface Y for each (x, z) column.
                    int terrainHeight = mapChunk.WorldGenTerrainHeightMap[localZ * chunkSize + localX];

                    // Brine pool depressions are positioned near sea level.
                    // Only process columns whose surface is at or just below sea level.
                    if (terrainHeight > seaLevel || terrainHeight < 2) continue;

                    int worldX = chunkX * chunkSize + localX;
                    int worldZ = chunkZ * chunkSize + localZ;

                    // Place still water in every air block from the terrain surface
                    // up to sea level, filling the depression.
                    for (int y = terrainHeight + 1; y <= seaLevel; y++)
                    {
                        BlockPos pos = new BlockPos(worldX, y, worldZ);
                        Block existing = worldGenBlockAccessor.GetBlock(pos);
                        if (existing.IsAir)
                        {
                            worldGenBlockAccessor.SetBlock(waterBlockId, pos);
                        }
                    }
                }
            }

            worldGenBlockAccessor.Commit();
        }
    }
}
