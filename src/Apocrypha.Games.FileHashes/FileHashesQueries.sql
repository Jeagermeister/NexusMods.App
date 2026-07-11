-- namespace: Apocrypha.Games.FileHashes
CREATE SCHEMA IF NOT EXISTS file_hashes;

-- ⚠️ TWO hash databases exist (linux fork): the read-only SHIPPED database registers as
-- DBName "hashes", and the writable local-recognition OVERLAY (written by
-- `steam recognize-game` / `steam local-index`) registers as "hashes_overlay". The query
-- engine resolves a DBName to a single connection, so every macro that reads game-file data
-- must consult BOTH names explicitly. Entity ids are database-scoped: manifest→file→hash
-- joins must stay WITHIN one database — resolve fully per-database, then UNION the results
-- (UNION, not UNION ALL: identical rows for versions present in both databases dedup).
-- Getting this wrong is catastrophic: the synchronizer's desired state comes from
-- file_hashes.loadout_files, and a version resolving to zero game files means applying that
-- loadout DELETES the whole install as "unwanted".

-- ENUM of all the store names
CREATE TYPE file_hashes.Stores AS ENUM ('Unknown', 'GOG', 'Steam', 'EA Desktop', 'Epic Games Store', 'Origin', 'Xbox Game Pass', 'Manually Added');

-- Find all the gog builds that match the given game's files, and rank them by the number of files that match
-- (GOG data exists only in the shipped database; the overlay writer is Steam-only today.)
CREATE MACRO file_hashes.resolve_gog_build(GameMetadataId, DefaultLanguage := 'en-US') AS TABLE
SELECT build.BuildId, ANY_VALUE(build.ProductId) AS BuildProductId, COUNT(*) matching_files, ANY_VALUE(build."version"), list_distinct(LIST(depot.ProductId)) ProductIds
FROM MDB_DISKSTATEENTRY() entry
         LEFT JOIN MDB_HASHRELATION(DBName=>"hashes") hashrel on entry.Hash = hashRel.xxHash3
         LEFT JOIN MDB_PATHHASHRELATION(DBName=>"hashes") pathrel on pathrel.Path = entry.Path.Item3 AND pathrel.Hash = hashrel.Id
         LEFT JOIN (SELECT Id, unnest(Files) FileId FROM MDB_GOGMANIFEST(DBName=>"hashes")) manifest on pathRel.Id = manifest.FileId
         LEFT JOIN MDB_GOGDEPOT(DBName=>"hashes") depot on depot.Manifest = Manifest.Id
         LEFT JOIN (SELECT Id, unnest(depots) depot, ProductId, buildId, "version" FROM MDB_GOGBUILD(DBName=>"hashes")) build on depot.Id = build.Depot
WHERE entry.Game = GameMetadataId
AND DefaultLanguage in depot.Languages
GROUP BY build.BuildId
ORDER BY COUNT(*) DESC;

-- Find all the steam manifests that match the given game's files, and rank them by the number of files that match.
-- Resolved per-database (shipped + overlay), unioned before ranking.
CREATE MACRO file_hashes.resolve_steam_manifests(GameMetadataId) AS TABLE
WITH matches AS (
    SELECT steam.DepotId DepotId, steam.AppId AppId, steam.ManifestId ManifestId, entry.Path.Item3 Path
    FROM MDB_DISKSTATEENTRY() entry
             JOIN MDB_HASHRELATION(DBName=>"hashes") hashrel on entry.Hash = hashRel.xxHash3
             JOIN MDB_PATHHASHRELATION(DBName=>"hashes") pathrel on pathrel.Path = entry.Path.Item3 AND pathrel.Hash = hashrel.Id
             JOIN (SELECT AppId, ManifestId, DepotId, unnest(Files) File FROM MDB_STEAMMANIFEST(DBName=>"hashes")) steam on steam.File = pathrel.Id
    WHERE entry.Game = GameMetadataId
    UNION
    SELECT steam.DepotId DepotId, steam.AppId AppId, steam.ManifestId ManifestId, entry.Path.Item3 Path
    FROM MDB_DISKSTATEENTRY() entry
             JOIN MDB_HASHRELATION(DBName=>"hashes_overlay") hashrel on entry.Hash = hashRel.xxHash3
             JOIN MDB_PATHHASHRELATION(DBName=>"hashes_overlay") pathrel on pathrel.Path = entry.Path.Item3 AND pathrel.Hash = hashrel.Id
             JOIN (SELECT AppId, ManifestId, DepotId, unnest(Files) File FROM MDB_STEAMMANIFEST(DBName=>"hashes_overlay")) steam on steam.File = pathrel.Id
    WHERE entry.Game = GameMetadataId
)
SELECT ANY_VALUE(DepotId) DepotId, COUNT(*) matching_count, ANY_VALUE(AppId) AppId, ANY_VALUE(ManifestId) ManifestId
FROM matches
GROUP BY ManifestId
ORDER BY COUNT(*) DESC;

-- Find all the depots (LocatorIds) for a given game. This will be the most matching depot for every AppId found in a given game folder
CREATE MACRO file_hashes.resolve_steam_depots(GameMetadataId) AS TABLE
SELECT arg_max(ManifestId, matching_count) DepotId
FROM file_hashes.resolve_steam_manifests(GameMetadataId) manifests
GROUP BY manifests.AppId
Having DepotId is not null;

-- gets all the loadouts, locatorids, and stores
CREATE MACRO file_hashes.loadout_locatorids(db) AS TABLE
SELECT install.Store::file_hashes.Stores Store, loadout.id Loadout, unnest(locatorIds) AS LocatorId
FROM MDB_LOADOUT(Db=>db) loadout
         LEFT JOIN MDB_GAMEINSTALLMETADATA(Db=>db) install on loadout.Installation = install.id;

-- Steam game files for loadouts, fully resolved WITHIN the shipped database
CREATE OR REPLACE MACRO file_hashes.steam_loadout_files_shipped(db) AS TABLE
SELECT files.Loadout, relations.Path, relations.Hash, relations.Size FROM
    (SELECT Loadout, unnest(Files) FileId
     FROM file_hashes.loadout_locatorids(db) locators
              JOIN MDB_STEAMMANIFEST(DbName=>'hashes') manifest ON manifest.ManifestId = locators.LocatorID::UBIGINT
     WHERE locators.Store = 'Steam') files
INNER JOIN
    (SELECT pathRel.Id, pathRel.Path, hashRel.xxHash3 Hash, hashRel.Size
     FROM MDB_PATHHASHRELATION(DBName=>'hashes') pathRel
     INNER JOIN MDB_HASHRELATION(DBName=>'hashes') hashRel ON pathRel.Hash = hashRel.Id) relations
ON files.FileId = relations.Id;

-- Steam game files for loadouts, fully resolved WITHIN the local-recognition overlay
CREATE OR REPLACE MACRO file_hashes.steam_loadout_files_overlay(db) AS TABLE
SELECT files.Loadout, relations.Path, relations.Hash, relations.Size FROM
    (SELECT Loadout, unnest(Files) FileId
     FROM file_hashes.loadout_locatorids(db) locators
              JOIN MDB_STEAMMANIFEST(DbName=>'hashes_overlay') manifest ON manifest.ManifestId = locators.LocatorID::UBIGINT
     WHERE locators.Store = 'Steam') files
INNER JOIN
    (SELECT pathRel.Id, pathRel.Path, hashRel.xxHash3 Hash, hashRel.Size
     FROM MDB_PATHHASHRELATION(DBName=>'hashes_overlay') pathRel
     INNER JOIN MDB_HASHRELATION(DBName=>'hashes_overlay') hashRel ON pathRel.Hash = hashRel.Id) relations
ON files.FileId = relations.Id;

-- gets all the paths and hashes of game files for gog loadouts
-- (shipped database only — the overlay writer is Steam-only today)
CREATE OR REPLACE MACRO file_hashes.gog_loadout_files(db) AS TABLE
WITH
  -- GOG locatorIds can contain a mix of BuildIds and DLC ProductIds
  locatorIds AS (SELECT Loadout, LocatorId::UBIGINT LocatorId
                 FROM file_hashes.loadout_locatorids(db) locators
                 WHERE locators.Store = 'GOG'),
  builds AS (SELECT Loadout, BuildId, ProductId BuildProductId, unnest(build.Depots) DepotId
             FROM MDB_GOGBUILD(DBName=>"hashes") build
             INNER JOIN locatorIds ON build.BuildId = locatorIds.LocatorId),
  validDepots AS (SELECT Id, ProductId, Manifest ManifestId
                  FROM MDB_GOGDEPOT(DBName=>"hashes")
                  WHERE Languages == [] OR 'en-US' in Languages),
  buildDepots AS (SELECT builds.Loadout, builds.DepotId, validDepots.ProductId DepotProductId, builds.BuildProductId, validDepots.ManifestId
                  FROM builds
                  JOIN validDepots on validDepots.Id = builds.DepotId),
  manifests AS (-- Depots for the base game product
                SELECT buildDepots.Loadout, buildDepots.ManifestId
                FROM buildDepots
                WHERE buildDepots.DepotProductId = buildDepots.BuildProductId
                UNION
                -- Depots for DLC products
                SELECT buildDepots.Loadout, buildDepots.ManifestId
                FROM buildDepots
                JOIN locatorIds dlcProducts ON dlcProducts.Loadout = buildDepots.Loadout AND buildDepots.DepotProductId = dlcProducts.LocatorId),
  files AS (SELECT Loadout, unnest(manifest.Files) File
            FROM manifests
            LEFT JOIN MDB_GOGMANIFEST(DBName=>"hashes") manifest ON manifests.ManifestId = manifest.Id)
SELECT files.Loadout, files.File PathId FROM files;

-- gets all the paths and hashes for game files in loadouts (shipped + overlay)
CREATE MACRO file_hashes.loadout_files(db) AS TABLE
WITH
       shipped_relations AS (SELECT pathRel.Id, pathRel.Path, hashRel.xxHash3 Hash, hashRel.Size
                  FROM MDB_PathHashRelation(DBName=>"hashes") pathRel
                  INNER JOIN MDB_hashrelation(DBName=>"hashes") hashRel ON pathRel.Hash = hashRel.Id)
SELECT gog_files.Loadout, relations.Path, relations.Hash, relations.Size
FROM file_hashes.gog_loadout_files(db) gog_files
INNER JOIN shipped_relations relations ON gog_files.PathId = relations.Id
UNION
SELECT Loadout, Path, Hash, Size FROM file_hashes.steam_loadout_files_shipped(db)
UNION
SELECT Loadout, Path, Hash, Size FROM file_hashes.steam_loadout_files_overlay(db);
