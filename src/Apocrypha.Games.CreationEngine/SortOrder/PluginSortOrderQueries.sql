-- namespace: Apocrypha.Games.CreationEngine.SortOrder

CREATE SCHEMA IF NOT EXISTS cengine;

-- Returns the plugin Sort Order Items for a given Sort Order Id
CREATE MACRO cengine.PluginSortOrderItems(db, sortOrderId) AS TABLE
SELECT s.PluginName, s.SortIndex, s.Id
FROM mdb_PluginSortOrderItem(Db=>db) s
WHERE s.ParentSortOrder = sortOrderId
ORDER BY s.SortIndex;


-- Return loadout mod groups that contain a `Data/<name>.(esp|esm|esl)` plugin file for a given loadout
CREATE MACRO cengine.LoadoutPluginGroups(db, loadoutId, gameLocationId) AS TABLE
SELECT
    regexp_extract(file.TargetPath.Item3, '^Data\/([^\/]+\.(esp|esm|esl))$', 1, 'i') AS PluginFileName,
    enabledState.IsEnabled AS IsEnabled,
    groupItem.Name AS ModName,
    groupItem.Id AS ModGroupId
FROM mdb_LoadoutItemWithTargetPath(Db=>db) as file
JOIN synchronizer.LeafLoadoutItems(db) as enabledState ON file.Id = enabledState.Id
JOIN mdb_LoadoutItemGroup(Db=>db) as groupItem ON file.Parent = groupItem.Id
WHERE file.TargetPath.Item1 = loadoutId
    AND file.TargetPath.Item2 = gameLocationId
    AND PluginFileName != '';


-- Return winning loadout plugin groups in case multiple mods ship the same plugin file.
-- Plugin file names are case-insensitive to the engine, so the partition folds case.
CREATE MACRO cengine.WinningLoadoutPluginGroups(db, loadoutId, gameLocationId) AS TABLE
SELECT
    PluginFileName,
    IsEnabled,
    ModName,
    ModGroupId
FROM (
         SELECT
             matchingMods.*,
             ROW_NUMBER() OVER (
                            PARTITION BY lower(matchingMods.PluginFileName)
                            ORDER BY matchingMods.IsEnabled DESC, matchingMods.ModGroupId DESC
                        ) AS ranking
         FROM cengine.LoadoutPluginGroups(db, loadoutId, gameLocationId) AS matchingMods
     ) ranked
WHERE ranking = 1
ORDER BY lower(PluginFileName) ASC;


-- Return the plugin Sort Order for a given loadout including the loadout data
CREATE MACRO cengine.PluginSortOrderWithLoadoutData(db, sortOrderId, loadoutId, gameLocationId) AS TABLE
SELECT
    sortItem.PluginName,
    sortItem.SortIndex,
    sortItem.Id,
    loadoutData.IsEnabled,
    loadoutData.ModName,
    loadoutData.ModGroupId
FROM mdb_PluginSortOrderItem(Db=>db) sortItem
LEFT OUTER JOIN cengine.WinningLoadoutPluginGroups(db, loadoutId, gameLocationId) loadoutData
    ON lower(sortItem.PluginName) = lower(loadoutData.PluginFileName)
WHERE sortItem.ParentSortOrder = sortOrderId
ORDER BY sortItem.SortIndex;
