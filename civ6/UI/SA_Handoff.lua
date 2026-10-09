-- Stellar Ascension: in-game UI context (Civ VI, AddUserInterfaces).
-- Hooks civ_handoff_dump and civ_handoff_popup (sheets/hooks.json).
-- When the local player completes the trigger project, it prints the handoff
-- block (records in sheets/handoff.json) to Lua.log for the launcher to read.
--@TRIGGERS@
--@DEFEAT_LEVELS@
--@TECHS@
--@MAX_RIVALS@
--@MAX_CITIES@

local m_done = false

local function Clean(s)
	s = tostring(s or "")
	s = s:gsub("[|\r\n]", " ")
	return s
end

local function Emit(...)
	local parts = { "SA" }
	for _, v in ipairs({ ... }) do table.insert(parts, Clean(v)) end
	print(table.concat(parts, "|"))
end

local function Lookup(key)
	if key == nil or key == "" then return "" end
	return Locale.Lookup(key)
end

local function Contains(list, value)
	for i, v in ipairs(list) do
		if v == value then return i end
	end
	return nil
end

local function SpaceRaceLevel()
	local v = GameConfiguration.GetValue("SA_SPACE_RACE")
	if v == nil or v == "" then return "standard" end
	return v
end

local function CivInfo(id)
	local cfg = PlayerConfigurations[id]
	local civType = cfg:GetCivilizationTypeName()
	local row = GameInfo.Civilizations[civType]
	local name = row and Lookup(row.Description) or Lookup(cfg:GetCivilizationDescription())
	local adjective = row and Lookup(row.Adjective) or Lookup(cfg:GetCivilizationShortDescription())
	local leader = Lookup(cfg:GetLeaderName())
	return name, adjective, leader
end

local function CityNames(player)
	local names, rest = {}, {}
	local capital = player:GetCities():GetCapitalCity()
	if capital ~= nil then table.insert(names, Lookup(capital:GetName())) end
	for _, city in player:GetCities():Members() do
		if capital == nil or city:GetID() ~= capital:GetID() then
			table.insert(rest, { Lookup(city:GetName()), city:GetPopulation() })
		end
	end
	table.sort(rest, function(a, b) return a[2] > b[2] end)
	for _, c in ipairs(rest) do
		if #names >= SA_MAX_CITIES then break end
		table.insert(names, c[1])
	end
	return names
end

local function WriteHandoff(localID)
	local order = Game:GetProperty("SA_SpaceOrder") or {}
	if not Contains(order, localID) then table.insert(order, localID) end
	local count = 0
	local function emit(...) Emit(...); count = count + 1 end

	emit("BEGIN", 1)
	emit("LEVEL", SpaceRaceLevel())

	-- the player first, then up to SA_MAX_RIVALS rivals
	local ids = { localID }
	for _, id in ipairs(PlayerManager.GetAliveMajorIDs()) do
		if id ~= localID and #ids <= SA_MAX_RIVALS then table.insert(ids, id) end
	end
	for slot, id in ipairs(ids) do
		local name, adjective, leader = CivInfo(id)
		emit("CIV", slot, (id == localID) and 1 or 0, Contains(order, id) or 0, name, adjective, leader)
		for _, city in ipairs(CityNames(Players[id])) do emit("CITY", slot, city) end
	end

	local techs = Players[localID]:GetTechs()
	for _, techType in ipairs(SA_TECHS) do
		local row = GameInfo.Technologies[techType]
		if row ~= nil and techs:HasTech(row.Index) then emit("TECH", techType) end
	end

	local seen = {}
	for _, city in Players[localID]:GetCities():Members() do
		local buildings = city:GetBuildings()
		for row in GameInfo.Buildings() do
			if row.IsWonder and not seen[row.BuildingType] and buildings:HasBuilding(row.Index) then
				seen[row.BuildingType] = true
				emit("WONDER", row.BuildingType, Lookup(row.Name), Lookup(row.Description))
			end
		end
	end

	local natural = {}
	for i = 0, Map.GetPlotCount() - 1 do
		local plot = Map.GetPlotByIndex(i)
		local f = plot:GetFeatureType()
		if f ~= nil and f >= 0 then
			local row = GameInfo.Features[f]
			if row ~= nil and row.NaturalWonder and not natural[row.FeatureType] then
				natural[row.FeatureType] = true
				emit("NATURAL", row.FeatureType, Lookup(row.Name))
			end
		end
	end
	Emit("END", count + 1)
end

local function ShowPopup(titleKey, bodyKey, arg)
	Controls.Title:SetText(Locale.Lookup(titleKey))
	Controls.Body:SetText(Locale.Lookup(bodyKey, arg))
	ContextPtr:SetHide(false)
end

local function OnCityProjectCompleted(playerID, cityID, projectID, buildingIndex, iX, iY, bCancelled)
	if bCancelled or m_done then return end
	local project = GameInfo.Projects[projectID]
	if project == nil or not SA_TRIGGERS[project.ProjectType] then return end
	local localID = Game.GetLocalPlayer()
	if localID == nil or localID < 0 then return end
	local order = Game:GetProperty("SA_SpaceOrder") or {}

	if playerID == localID then
		m_done = true
		local ok, err = pcall(WriteHandoff, localID)
		if not ok then print("SA_EVENT|HANDOFF_ERROR|" .. tostring(err)) end
		local name = CivInfo(localID)
		ShowPopup("LOC_SA_ACT1_TITLE", "LOC_SA_ACT1_BODY", name)
	elseif SA_DEFEAT_LEVELS[SpaceRaceLevel()] and not Contains(order, localID) and Players[playerID]:IsMajor() then
		m_done = true
		local name = CivInfo(playerID)
		Emit("BEGIN", 1)
		Emit("DEFEAT", name)
		Emit("END", 3)
		ShowPopup("LOC_SA_DEFEAT_TITLE", "LOC_SA_DEFEAT_BODY", name)
	end
end

function OnOK()
	ContextPtr:SetHide(true)
end

function Initialize()
	ContextPtr:SetHide(true)
	Controls.OKButton:RegisterCallback(Mouse.eLClick, OnOK)
	Events.CityProjectCompleted.Add(OnCityProjectCompleted)
	print("SA_EVENT|UI_LOADED")
end
Initialize()
