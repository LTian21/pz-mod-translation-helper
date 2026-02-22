---@diagnostic disable: duplicate-set-field
require "TimedActions/ISReloadWeaponAction"
require "TimedActions/ISUnloadBulletsFromFirearm"
require "TimedActions/ISRackFirearm"
require "TimeActions/ISReloadWeaponAction"

NepSurRifle = {}
NepSurRifle.Debug = true
NepSurRifle.ModelMain = "NepSurRifle"
NepSurRifle.ModelFolded = "NepSurRifle_FOLDED"
NepSurRifle.ModelOpen = "NepSurRifle_OPEN"

--NepSurRifle.AmmoTypes = {"Base.223Bullets", "Base.556Bullets"}
NepSurRifle.AmmoTypes4213 = {
        Ammo223 = {item="Base.223Bullets", ammo = AmmoType.BULLETS_223},
        Ammo556 = {item="Base.556Bullets", ammo = AmmoType.BULLETS_556},
    }

NepSurRifle.WeaponFullName = "Base.NepSurRifle"
NepSurRifle.Attachments={
    "Base.DIYSuppressor",
    "Base.223556Suppressor",
    "Base.x2Scope",
    "Base.x4Scope",
    "Base.x8Scope",
    "Base.x4ACOGScope",
    "Base.x8ACOGScope",
    "Base.Laser",
    "Base.RedDot",
    "Base.SOCOMRedDot",
}

NepSurRifle.rifleObject = nil -- for tracking the gun to fold/unfold.
NepSurRifle.hasExplained = false

function NepSurRifle.DebugSay(message)
    if NepSurRifle.Debug then
        print("NepSurRifle: "..message)
    end
end


-- ----------------------------------------------------------------------------------------------------------------------

-- Add an extra weapon to a weaponpart's list of weapons it can be attached to
-- The weapon must have an appropriate ModelWeaponPart line!
function NepSurRifle.AddMount(weaponpart, weapon) -- can use ("Base.Attachment", "MyGun") or a fully qualified weapon name.
    NepSurRifle.DebugSay("Adding "..weapon.." to "..weaponpart)        
    
    local item = instanceItem(weaponpart) -- only instanceItem works if you wnat to use getMountOn()!
    if item == nil then
		if not NepSurRifle.hasExplained then
			NepSurRifle.DebugSay("NOTE: It is normal to see messages about \"unable to find item\" as this mod checks for items from other mods")
			NepSurRifle.hasExplained=true
		end
        NepSurRifle.DebugSay("Unable to find item "..weaponpart)
        return
    end        
    if item.getMountOn == nil then
        NepSurRifle.DebugSay("Item "..weaponpart.." does not support getMountOn()")
        return
    end
    local script = ScriptManager.instance:getItem(weaponpart) --lets just assume this works if item passed the last two checks
    local newList="MountOn = " -- string for DoParam
    local mounts = item:getMountOn()
    if mounts ~= nil and mounts:size() > 0 then 
        for i=1,mounts:size() do
            local nextGun=mounts:get(i-1)
            if nextGun == weapon or nextGun == ("Base."..weapon) then -- Weapon not in Base namespace would have full hame given
                NepSurRifle.DebugSay(weapon.." is already in MountOn list for "..weaponpart.."!")
                return
            end
            newList=newList.. nextGun .. "; "
        end 
    end
    newList=newList..weapon 
    if NepSurRifle.PrintNewMounts then NepSurRifle.DebugSay(newList) end 
    script:DoParam(newList)
end

-- ----------------------------------------------------
-- Add my gun to the weapon list for existing attachments
function NepSurRifle.AdjustAttachments()
    for k,v in ipairs(NepSurRifle.Attachments) do 
        NepSurRifle.AddMount(v,NepSurRifle.WeaponFullName)
    end
end
Events.OnGameBoot.Add(NepSurRifle.AdjustAttachments) 


-- ----------------------------------------------------------------------------------------------------------------------
-- Code to handle folding/unfolding the rifle.  Unfolding when equipped is easy, but we need to store the rifle object in a 
-- variable so we can fold it after it is removed since there is no handy OnUnequipItem event.

function NepSurRifle.OnEquipPrimary(player, item)
    local isSurvivalRifle = (item ~= nil and item:getFullType() == NepSurRifle.WeaponFullName)
    local alreadyTrackingSomething = (NepSurRifle.rifleObject ~= nil)

    if alreadyTrackingSomething and item == NepSurRifle.rifleObject then --we requipped the tracked item somehow. Unfold it to make sure it's unfolded.
        item:setWeaponSprite(NepSurRifle.ModelMain)
        player:resetEquippedHandsModels()
    elseif alreadyTrackingSomething and isSurvivalRifle then --we're carrying two rifles, and swapped them. Fold the previously tracked item, unfold the new one and start tracking it.
        NepSurRifle.rifleObject:setWeaponSprite(NepSurRifle.ModelFolded)
        item:setWeaponSprite(NepSurRifle.ModelMain)
        player:resetEquippedHandsModels()
        NepSurRifle.rifleObject = item
    elseif alreadyTrackingSomething and not isSurvivalRifle then -- we put our rifle away. fold it and stop tracking.
        NepSurRifle.rifleObject:setWeaponSprite(NepSurRifle.ModelFolded)
        NepSurRifle.rifleObject = nil
    elseif not alreadyTrackingSomething and isSurvivalRifle then --we were not holding a survival rifle, now we are, so unfold and track it.
        item:setWeaponSprite(NepSurRifle.ModelMain)
        player:resetEquippedHandsModels()
        NepSurRifle.rifleObject = item
    end
end
Events.OnEquipPrimary.Add(NepSurRifle.OnEquipPrimary)

-- Just in case a player starts the game holding a survival rifle, we need to track it aso it can be folded when removed.
function NepSurRifle.OnCreatePlayer(playerNum, player)
    NepSurRifle.OnEquipPrimary(player, player:getPrimaryHandItem())
end
Events.OnCreatePlayer.Add(NepSurRifle.OnCreatePlayer)




-- ----------------------------------------------------------------------------------------------------------------------
-- This is handle replacing the weapon model when reloading.  Using the standard "Double Barrel Shotgun" method does a 
-- temporary override of the item's model that doesn't have any attachments, so it looks very odd when a scope is involved.
-- So I renamed the event in the animation file changeWeaponSprite -> changeWeaponSpriteSurRifle, added a handler to open it 
-- when this is received, and when the completes or stops the model is reset.  And I'll have to do teh same for unload, maybe?
-- Actually rack, not unload, if it's only one round.

-- Set model to closed & force update of held items
function NepSurRifle.Close(player)
    if NepSurRifle.rifleObject ~= nil then 
        --NepSurRifle.DebugSay("##### Closing rifle")
        NepSurRifle.rifleObject:setWeaponSprite(NepSurRifle.ModelMain)
        player:resetEquippedHandsModels()
    end
end

-- Set model to open & force update of held items
function NepSurRifle.Open(player)
    if NepSurRifle.rifleObject ~= nil then 
        --NepSurRifle.DebugSay("##### Opening rifle")
        NepSurRifle.rifleObject:setWeaponSprite(NepSurRifle.ModelOpen)
        player:resetEquippedHandsModels()
    end
end

----- loading -----
-- This works in SP & MP and I'm not going to bother asking why
-- shoudl be able to make this revert the model change too - see IsReloadWeaponAction.lua  ISReloadWeaponAction:animEvent()
ISReloadWeaponAction.NepOG_animEvent = ISReloadWeaponAction.animEvent
function ISReloadWeaponAction.animEvent(self, event, parameter)
    --NepSurRifle.DebugSay(string.format("----- ISReloadWeaponAction.animEvent event: %s Tracking: %s",tostring(event), tostring(NepSurRifle.rifleObject ~= nil)))
    if tostring(event) == 'changeWeaponSprite' and NepSurRifle.rifleObject ~= nil then
        NepSurRifle.Open(self.character)
    else
        ISReloadWeaponAction.NepOG_animEvent(self, event, parameter)
    end
end


ISReloadWeaponAction.NepOG_perform = ISReloadWeaponAction.perform
function ISReloadWeaponAction.perform(self)
    if not isServer() then --SP, MP Client
        if NepSurRifle.rifleObject ~= nil then 
            NepSurRifle.Close(self.character)
        end
    end
    return ISReloadWeaponAction.NepOG_perform(self)
end


ISReloadWeaponAction.NepOG_complete = ISReloadWeaponAction.complete
function ISReloadWeaponAction.complete(self)
    if isServer() then --MP server
        if NepSurRifle.rifleObject ~= nil then 
            NepSurRifle.Close(self.character)
        end
    end
    return ISReloadWeaponAction.NepOG_complete(self)
end

ISReloadWeaponAction.NepOG_stop = ISReloadWeaponAction.stop
function ISReloadWeaponAction.stop(self)
    if NepSurRifle.rifleObject ~= nil then 
        NepSurRifle.Close(self.character)
    end
    ISReloadWeaponAction.NepOG_stop(self)
end

----- Unloading -----

ISUnloadBulletsFromFirearm.NepOG_animEvent = ISUnloadBulletsFromFirearm.animEvent
function ISUnloadBulletsFromFirearm.animEvent(self, event, parameter)
    --NepSurRifle.DebugSay(string.format("----- ISUnloadBulletsFromFirearm.animEvent.animEvent event: %s Tracking: %s",tostring(event), tostring(NepSurRifle.rifleObject ~= nil)))
    if tostring(event) == 'changeWeaponSprite' and NepSurRifle.rifleObject ~= nil then
        NepSurRifle.Open(self.character)
    else        
        ISUnloadBulletsFromFirearm.NepOG_animEvent(self, event, parameter)
    end
end

ISUnloadBulletsFromFirearm.NepOG_complete = ISUnloadBulletsFromFirearm.complete
function ISUnloadBulletsFromFirearm.complete(self)
    NepSurRifle.Close(self.character)
	return ISUnloadBulletsFromFirearm.NepOG_complete(self)
end

ISUnloadBulletsFromFirearm.NepOG_stop =ISUnloadBulletsFromFirearm.stop
function ISUnloadBulletsFromFirearm.stop(self)
    NepSurRifle.Close(self.character)    
    ISUnloadBulletsFromFirearm.NepOG_stop(self)
end


----- Racking -----
ISRackFirearm.NepOG_animEvent = ISRackFirearm.animEvent
function ISRackFirearm.animEvent(self, event, parameter)
    --NepSurRifle.DebugSay(string.format("----- ISRackFirearm.animEvent.animEvent event: %s Tracking: %s",tostring(event), tostring(NepSurRifle.rifleObject ~= nil)))
    if tostring(event) == 'changeWeaponSprite' and NepSurRifle.rifleObject ~= nil then
        NepSurRifle.Open(self.character)
    else 
        ISRackFirearm.NepOG_animEvent(self, event, parameter)
    end
end

ISRackFirearm.NepOG_complete = ISRackFirearm.complete
function ISRackFirearm.complete(self)
    NepSurRifle.Close(self.character)    
	return ISRackFirearm.NepOG_complete(self)
end

ISRackFirearm.NepOG_stop =ISRackFirearm.stop
function ISUnloadBulletsFromFirearm.stop(self)
    NepSurRifle.Close(self.character)    
    ISRackFirearm.NepOG_stop(self)
end


