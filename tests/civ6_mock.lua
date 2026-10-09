-- A small stand-in for the Civ VI Lua API, just enough to run Stellar Ascension's
-- scripts outside the game. Shapes follow the Civ VI modding knowledge base
-- (sukritact/Civilization-VI-Modding-Knowledge-Base). It is a test double: the
-- real check is the running game.
LOG = {}
function print(...)
	local t = {}
	for _, v in ipairs({ ... }) do table.insert(t, tostring(v)) end
	table.insert(LOG, (CONTEXT or "?") .. ": " .. table.concat(t, "\t"))
end

local function Indexed(rows, key)
	local t = {}
	for i, r in ipairs(rows) do r.Index = i - 1; t[i - 1] = r; t[r[key]] = r end
	setmetatable(t, { __call = function()
		local i = 0
		return function() i = i + 1; return rows[i] end
	end })
	return t
end

GameInfo = {
	Projects = Indexed({ { ProjectType = "PROJECT_LAUNCH_EARTH_SATELLITE" }, { ProjectType = "PROJECT_LAUNCH_MOON_LANDING" } }, "ProjectType"),
	Victories = Indexed({ { VictoryType = "VICTORY_DEFAULT" }, { VictoryType = "VICTORY_TECHNOLOGY" } }, "VictoryType"),
	Technologies = Indexed({ { TechnologyType = "TECH_POTTERY" }, { TechnologyType = "TECH_ASTROLOGY" }, { TechnologyType = "TECH_WRITING" }, { TechnologyType = "TECH_ROCKETRY" } }, "TechnologyType"),
	Buildings = Indexed({ { BuildingType = "BUILDING_MONUMENT", Name = "LOC_BUILDING_MONUMENT_NAME" },
		{ BuildingType = "BUILDING_PYRAMIDS", IsWonder = true, Name = "LOC_BUILDING_PYRAMIDS_NAME", Description = "LOC_BUILDING_PYRAMIDS_DESCRIPTION" },
		{ BuildingType = "BUILDING_BIG_BEN", IsWonder = true, Name = "LOC_BUILDING_BIG_BEN_NAME" },
		{ BuildingType = "BUILDING_MACHU_PICCHU", IsWonder = true, Name = "LOC_BUILDING_MACHU_PICCHU_NAME" } }, "BuildingType"),
	Features = Indexed({ { FeatureType = "FEATURE_FOREST" }, { FeatureType = "FEATURE_EVEREST", NaturalWonder = true, Name = "LOC_FEATURE_EVEREST_NAME" },
		{ FeatureType = "FEATURE_PAITITI", NaturalWonder = true, Name = "LOC_FEATURE_PAITITI_NAME" } }, "FeatureType"),
	Civilizations = Indexed({ { CivilizationType = "CIVILIZATION_ROME", Description = "LOC_CIVILIZATION_ROME_DESCRIPTION", Adjective = "LOC_CIVILIZATION_ROME_ADJECTIVE" },
		{ CivilizationType = "CIVILIZATION_JAPAN", Description = "LOC_CIVILIZATION_JAPAN_DESCRIPTION", Adjective = "LOC_CIVILIZATION_JAPAN_ADJECTIVE" },
		{ CivilizationType = "CIVILIZATION_SCYTHIA", Description = "LOC_CIVILIZATION_SCYTHIA_DESCRIPTION", Adjective = "LOC_CIVILIZATION_SCYTHIA_ADJECTIVE" } }, "CivilizationType"),
}

local TEXT = {
	LOC_CIVILIZATION_ROME_DESCRIPTION = "Roman Empire", LOC_CIVILIZATION_ROME_ADJECTIVE = "Roman", LOC_CIVILIZATION_ROME_NAME = "Rome",
	LOC_CIVILIZATION_JAPAN_DESCRIPTION = "Japanese Empire", LOC_CIVILIZATION_JAPAN_ADJECTIVE = "Japanese", LOC_CIVILIZATION_JAPAN_NAME = "Japan",
	LOC_CIVILIZATION_SCYTHIA_DESCRIPTION = "Scythian Empire", LOC_CIVILIZATION_SCYTHIA_ADJECTIVE = "Scythian", LOC_CIVILIZATION_SCYTHIA_NAME = "Scythia",
	LOC_LEADER_TRAJAN_NAME = "Trajan", LOC_LEADER_HOJO_NAME = "Hojo Tokimune", LOC_LEADER_TOMYRIS_NAME = "Tomyris",
	LOC_BUILDING_PYRAMIDS_NAME = "Pyramids", LOC_BUILDING_PYRAMIDS_DESCRIPTION = "+25% Production toward districts.",
	LOC_BUILDING_BIG_BEN_NAME = "Big Ben", LOC_BUILDING_MACHU_PICCHU_NAME = "Machu Picchu",
	LOC_FEATURE_EVEREST_NAME = "Mount Everest", LOC_FEATURE_PAITITI_NAME = "Paititi",
	LOC_CITY_NAME_ROME = "Rome", LOC_CITY_NAME_ANTIUM = "Antium", LOC_CITY_NAME_CUMAE = "Cumae|pipe", LOC_CITY_NAME_KYOTO = "Kyōto",
	LOC_SA_ACT1_BODY = "Act 1 done, pick {1_Name}", LOC_SA_ACT1_TITLE = "Act 1", LOC_SA_DEFEAT_TITLE = "Lost", LOC_SA_DEFEAT_BODY = "{1_Name} won",
	LOC_SA_RIVAL_SPACE_TITLE = "Rival", LOC_SA_RIVAL_SPACE_BODY = "{1_Name} first",
}
Locale = { Lookup = function(key, a) local s = TEXT[key] or key; if a then s = s:gsub("{1_Name}", a) end; return s end }

SETUP = { SA_SPACE_RACE = "standard" }
GameConfiguration = { GetValue = function(k) return SETUP[k] end }

local PROPS = {}
WINNER = nil
Game = {
	GetProperty = function(self, k) return PROPS[k] end,
	SetProperty = function(self, k, v) PROPS[k] = v end,
	GetLocalPlayer = function() return 0 end,
	SetWinningTeam = function(team, victory) WINNER = { team, victory } end,
}

local function City(id, nameKey, pop, wonders)
	local has = {}
	for _, w in ipairs(wonders or {}) do has[GameInfo.Buildings[w].Index] = true end
	return { GetID = function() return id end, GetName = function() return nameKey end, GetPopulation = function() return pop end,
		GetBuildings = function() return { HasBuilding = function(self, i) return has[i] == true end } end }
end
local function Cities(list)
	return { GetCapitalCity = function() return list[1] end,
		Members = function() local i = 0; return function() i = i + 1; if list[i] then return i, list[i] end end end }
end
local function Player(id, human, cities, techs)
	local known = {}
	for _, t in ipairs(techs or {}) do known[GameInfo.Technologies[t].Index] = true end
	return { IsHuman = function() return human end, IsMajor = function() return true end, GetTeam = function() return 100 + id end,
		GetCities = function() return Cities(cities) end,
		GetTechs = function() return { HasTech = function(self, i) return known[i] == true end } end }
end
Players = {
	[0] = Player(0, true, { City(1, "LOC_CITY_NAME_ROME", 12, { "BUILDING_PYRAMIDS" }), City(2, "LOC_CITY_NAME_ANTIUM", 5), City(3, "LOC_CITY_NAME_CUMAE", 9, { "BUILDING_BIG_BEN", "BUILDING_MACHU_PICCHU" }) }, { "TECH_ASTROLOGY", "TECH_ROCKETRY" }),
	[1] = Player(1, false, { City(1, "LOC_CITY_NAME_KYOTO", 10) }),
	[2] = Player(2, false, {}),
}
PlayerManager = { GetAliveMajorIDs = function() return { 0, 1, 2 } end }
local CFG = { [0] = { "CIVILIZATION_ROME", "LOC_LEADER_TRAJAN_NAME" }, [1] = { "CIVILIZATION_JAPAN", "LOC_LEADER_HOJO_NAME" }, [2] = { "CIVILIZATION_SCYTHIA", "LOC_LEADER_TOMYRIS_NAME" } }
PlayerConfigurations = setmetatable({}, { __index = function(_, id)
	local c = CFG[id]
	return { GetCivilizationTypeName = function() return c[1] end, GetLeaderName = function() return c[2] end,
		GetCivilizationShortDescription = function() return c[1]:gsub("CIVILIZATION_", "LOC_CIVILIZATION_") .. "_NAME" end,
		GetCivilizationDescription = function() return c[1]:gsub("CIVILIZATION_", "LOC_CIVILIZATION_") .. "_DESCRIPTION" end }
end })

local PLOTS = { 0, -1, 1, 1, 2, -1 }
Map = { GetPlotCount = function() return #PLOTS end,
	GetPlotByIndex = function(i) return { GetFeatureType = function() return PLOTS[i + 1] end } end }

NOTIFICATIONS = {}
NotificationTypes = { USER_DEFINED_1 = 1 }
NotificationManager = { SendNotification = function(p, t, title, body) table.insert(NOTIFICATIONS, title .. ": " .. body) end }

HANDLERS = {}
Events = setmetatable({}, { __index = function(t, name)
	local e = { Add = function(fn) HANDLERS[name] = HANDLERS[name] or {}; table.insert(HANDLERS[name], fn) end }
	rawset(t, name, e)
	return e
end })
function Fire(name, ...)
	for _, fn in ipairs(HANDLERS[name] or {}) do fn(...) end
end

HIDDEN = true
ContextPtr = { SetHide = function(self, h) HIDDEN = h end }
local function Ctl() return { SetText = function(self, s) self.text = s end, RegisterCallback = function() end } end
Controls = { Title = Ctl(), Body = Ctl(), OKButton = Ctl() }
Mouse = { eLClick = 1 }
