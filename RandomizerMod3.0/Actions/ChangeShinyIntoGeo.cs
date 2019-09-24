﻿using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using RandomizerMod.FsmStateActions;
using SeanprCore;
using UnityEngine;

namespace RandomizerMod.Actions
{
    public class ChangeShinyIntoGeo : RandomizerAction
    {
        private readonly string _boolName;
        private readonly string _fsmName;
        private readonly int _geoAmount;
        private readonly string _objectName;
        private readonly string _sceneName;
        private readonly string _location;

        public ChangeShinyIntoGeo(string sceneName, string objectName, string fsmName, string boolName, int geoAmount, string location)
        {
            _sceneName = sceneName;
            _objectName = objectName;
            _fsmName = fsmName;
            _boolName = boolName;
            _geoAmount = geoAmount;
            _boolName = boolName;
            _location = location;
        }

        public override ActionType Type => ActionType.PlayMakerFSM;

        public override void Process(string scene, Object changeObj)
        {
            if (scene != _sceneName || !(changeObj is PlayMakerFSM fsm) || fsm.FsmName != _fsmName ||
                fsm.gameObject.name != _objectName)
            {
                return;
            }

            FsmState pdBool = fsm.GetState("PD Bool?");
            FsmState charm = fsm.GetState("Charm?");
            FsmState getCharm = fsm.GetState("Get Charm");

            // Remove actions that stop shiny from spawning
            pdBool.RemoveActionsOfType<PlayerDataBoolTest>();
            pdBool.RemoveActionsOfType<StringCompare>();

            // Add our own check to stop the shiny from being grabbed twice
            pdBool.AddAction(new RandomizerBoolTest(_boolName, null, "COLLECTED"));

            // The "Charm?" state is a bad entry point for our geo spawning
            charm.ClearTransitions();
            charm.AddTransition("FINISHED", "Get Charm");
            // The "Get Charm" state is a good entry point for our geo spawning
            getCharm.RemoveActionsOfType<SetPlayerDataBool>();
            getCharm.RemoveActionsOfType<IncrementPlayerDataInt>();
            getCharm.RemoveActionsOfType<SendMessage>();

            getCharm.AddAction(new RandomizerExecuteLambda(() => RandoLogger.UpdateHelperLog()));
            getCharm.AddAction(new RandomizerExecuteLambda(() => RandoLogger.LogItemToTrackerByBoolName(_boolName, _location)));
            getCharm.AddFirstAction(new RandomizerExecuteLambda(() => RandomizerMod.Instance.Settings.UpdateObtainedProgressionByBoolName(_boolName)));
            getCharm.AddAction(new RandomizerSetBool(_boolName, true));
            getCharm.AddAction(new RandomizerAddGeo(fsm.gameObject, _geoAmount));

            // Skip all the other type checks
            getCharm.ClearTransitions();
            getCharm.AddTransition("FINISHED", "Flash");
        }
    }
}