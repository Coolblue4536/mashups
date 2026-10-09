-- Stellar Ascension: gameplay script (Civ VI, AddGameplayScripts).
-- Hooks civ_space_order and civ_ruthless_defeat (sheets/hooks.json).
-- Data tables below are filled in from the sheets by tools/gen_civ6.py.
--@TRIGGERS@
--@DEFEAT_LEVELS@

local function SpaceRaceLevel()
	local v = GameConfiguration.GetValue("SA_SPACE_RACE")
	if v == nil or v == "" then return "standard" end
	return v
end

local function Contains(list, value)
	for _, v in ipairs(list) do
		if v == value then return true end
	end
	return false
end

local function HumanPlayer()
	for _, id in ipairs(PlayerManager.GetAliveMajorIDs()) do
		if Players[id]:IsHuman() then return id end
	end
	return nil
end

local function OnCityProjectCompleted(playerID, cityID, projectID, buildingIndex, iX, iY, bCancelled)
	if bCancelled then return end
	local project = GameInfo.Projects[projectID]
	if project == nil or not SA_TRIGGERS[project.ProjectType] then return end
	local player = Players[playerID]
	if player == nil or not player:IsMajor() then return end

	local order = Game:GetProperty("SA_SpaceOrder") or {}
	if Contains(order, playerID) then return end
	table.insert(order, playerID)
	Game:SetProperty("SA_SpaceOrder", order)
	print("SA_EVENT|SPACE|" .. playerID .. "|" .. #order)

	local human = HumanPlayer()
	if human == nil or playerID == human or Contains(order, human) then return end

	-- A rival got there before the player.
	local cfg = PlayerConfigurations[playerID]
	local civ = GameInfo.Civilizations[cfg:GetCivilizationTypeName()]
	local name = Locale.Lookup(civ and civ.Description or cfg:GetCivilizationShortDescription())
	pcall(function()
		NotificationManager.SendNotification(human, NotificationTypes.USER_DEFINED_1,
			Locale.Lookup("LOC_SA_RIVAL_SPACE_TITLE"), Locale.Lookup("LOC_SA_RIVAL_SPACE_BODY", name), iX, iY)
	end)

	if SA_DEFEAT_LEVELS[SpaceRaceLevel()] and Game:GetProperty("SA_Defeated") == nil then
		Game:SetProperty("SA_Defeated", playerID)
		local victory = GameInfo.Victories["VICTORY_TECHNOLOGY"]
		local ok, err = pcall(function()
			Game.SetWinningTeam(player:GetTeam(), victory and victory.Index or 0)
		end)
		print("SA_EVENT|RUTHLESS|" .. playerID .. "|" .. tostring(ok) .. "|" .. tostring(err))
	end
end

Events.CityProjectCompleted.Add(OnCityProjectCompleted)
print("SA_EVENT|LOADED|" .. SpaceRaceLevel())
