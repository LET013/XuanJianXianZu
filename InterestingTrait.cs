using System;
using NeoModLoader.api;
using XuanJianVNext.Architecture.Bootstrap;
using XuanJianVNext.Core;

namespace XuanJianVNext
{
    internal class XuanJianMod : BasicMod<XuanJianMod>
    {
        public static string id = "shiyue.worldbox.mod.XuanJian";

        protected override void OnModLoad()
        {
            try
            {
                XjModBootstrap.Initialize(
                    id,
                    () => XjRuntimeSettings.LoadFromModConfig(GetConfig()));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error during mod loading: {ex}");
            }
        }
    }
}
