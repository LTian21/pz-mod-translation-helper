-- ************************************************************************
-- **        ██████  ██████   █████  ██    ██ ███████ ███    ██          **
-- **        ██   ██ ██   ██ ██   ██ ██    ██ ██      ████   ██          **
-- **        ██████  ██████  ███████ ██    ██ █████   ██ ██  ██          **
-- **        ██   ██ ██   ██ ██   ██  ██  ██  ██      ██  ██ ██          **
-- **        ██████  ██   ██ ██   ██   ████   ███████ ██   ████          **
-- ************************************************************************
-- ** All rights reserved. This content is protected by © Copyright law. **
-- ************************************************************************

BB_StairsAlert_Utils = {}

BB_StairsAlert_Utils.GetGameSpeed = function()
    if getWorld():getGameMode() == "Multiplayer" then return 1 end
    local speedControl = UIManager.getSpeedControls():getCurrentGameSpeed()
    local gameSpeed = 1

    if speedControl == 2 then
        gameSpeed = 5
    elseif speedControl == 3 then
        gameSpeed = 20
    elseif speedControl == 4 then
        gameSpeed = 40
    end

    return gameSpeed
end

BB_StairsAlert_Utils.DelayFunction = function(func, delay, adaptToSpeed)

    delay = delay or 1
    local multiplier = 1
    local ticks = 0
    local canceled = false
    local tickRate = 60
    local lastTickTime = os.time()

    local function onTick()
        local currentTime = os.time()
        local deltaTime = currentTime - lastTickTime
        lastTickTime = currentTime

        if adaptToSpeed then multiplier = BB_StairsAlert_Utils.GetGameSpeed() end
        if not canceled and ticks < delay then
            ticks = (ticks + multiplier) + deltaTime * tickRate
            return
        end

        Events.OnTick.Remove(onTick)
        if not canceled then func() end
    end

    Events.OnTick.Add(onTick)
    return function()
        canceled = true
    end
end