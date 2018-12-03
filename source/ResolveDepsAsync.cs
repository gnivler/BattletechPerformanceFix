﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BattleTech.Data;
using BattleTech;
using BattleTech.Assetbundles;
using Harmony;
using UnityEngine;
using System.IO;
using SVGImporter;
using HBS.Data;
using RSG;
using System.Diagnostics;
using System.Reflection;
using BattleTech;
using BattleTech.Portraits;
using BattleTech.Framework;
using RT = BattleTech.BattleTechResourceType;
using BattleTech.Rendering.MechCustomization;
using static BattletechPerformanceFix.Control;

namespace BattletechPerformanceFix
{
    /* Just to set the load request for the dependency verification */
    class DummyLoadRequest : DataManager.ResourceLoadRequest<object>
    {
        public DummyLoadRequest(DataManager dataManager) : base(dataManager, RT.AbilityDef, "dummy_load_request_for_weight", 10000, null) { }
        public override bool AlreadyLoaded { get => true; }
    }

    static class ResolveExt
    {
        public static IPromise Resolve(this DataManager.ILoadDependencies cls, DataManager dm)
        {
            LogDebug("Attempt to resolve {0}", cls.GetType());
            var prom = new Promise();
            if (dm == null)
                LogError("DM is null");
            cls.DataManager = dm;
            cls.RequestDependencies(cls.DataManager, prom.Resolve, new DummyLoadRequest(dm));
            return prom;
        }

        public static IPromise OfString(this string str, RT type)
            => string.IsNullOrEmpty(str) ? Promise.Resolved() : ResolveDepsAsync.Load(type, str);
    }

    class ResolveDepsAsync : Feature
    {
        public static bool WantVerify = false;
        public void Activate()
        {
            var wantTracking = true;

            var t = typeof(ResolveDepsAsync);
            harmony.Patch(AccessTools.Method(typeof(BattleTechResourceLocator), "RefreshTypedEntries"), null, new HarmonyMethod(AccessTools.Method(t, nameof(IntegrityCheck))));

            if (WantVerify || wantTracking)
            {
                Log("Tracking is ON");
                var pre = new HarmonyMethod(AccessTools.Method(t, "TrackPre"));
                HarmonyMethod post = null; // new HarmonyMethod(AccessTools.Method(t, "TrackPost"));
                Assembly.GetAssembly(typeof(HeraldryDef))
                    .GetTypes()
                    .Where(ty => ty.GetInterface(typeof(DataManager.ILoadDependencies).FullName) != null)
                    .ForEach(ild =>
                    {

                        harmony.Patch(AccessTools.Method(ild, "CheckDependenciesAfterLoad"), pre, post);
                        harmony.Patch(AccessTools.Method(ild, "DependenciesLoaded"), pre, post);
                        harmony.Patch(AccessTools.Method(ild, "RequestDependencies"), pre, post);
                    });

                Assembly.GetAssembly(typeof(DataManager))
                    .GetTypes()
                    .Where(ty => ty.FullName.EndsWith("SpriteLoadRequest"))
                    .ForEach(ty => harmony.Patch(AccessTools.Method(ty, "Load"), pre, post));

                AccessTools.GetDeclaredMethods(typeof(Resources))
                    .Where(meth => meth.Name == "Load" && !meth.IsGenericMethod && !meth.IsGenericMethodDefinition && meth.GetMethodBody() != null)
                    .ForEach(meth => harmony.Patch(meth, new HarmonyMethod(AccessTools.Method(t, nameof(FileHookPath)))));

                AccessTools.GetDeclaredMethods(typeof(AssetBundle))
                    .Where(meth => meth.Name == "LoadAsset" && !meth.IsGenericMethod && !meth.IsGenericMethodDefinition && meth.GetMethodBody() != null)
                    .ForEach(meth => harmony.Patch(meth, new HarmonyMethod(AccessTools.Method(t, nameof(FileHookName)))));
                harmony.Patch(AccessTools.Method(typeof(DataLoader), "CallHandler"), new HarmonyMethod(AccessTools.Method(t, nameof(FileHookPath))));
                // Also check GenerateWebRequest GenerateWebRequest


                harmony.Patch(AccessTools.Method(typeof(BattleTech.UI.SimGameOptionsMenu), "OnAddedToHierarchy"), new HarmonyMethod(AccessTools.Method(t, "Summary")));
                harmony.Patch(AccessTools.Method(typeof(DataManager), "RequestResource_Internal"), new HarmonyMethod(AccessTools.Method(t, nameof(TrackRequestResource))));


                //harmony.Patch(AccessTools.Method(typeof(DataManager), "Update"), new HarmonyMethod(AccessTools.Method(t, nameof(DataManager_Update))));
            }

            /*
            harmony.Patch(AccessTools.Method(typeof(HBS.Threading.SimpleThreadPool), "Worker"), new HarmonyMethod(drop));
            harmony.Patch(AccessTools.Method(typeof(BattleTech.UI.AVPVideoPlayer), "ForcePlayVideo"), new HarmonyMethod(drop));
            harmony.Patch(AccessTools.Method(typeof(BattleTech.UI.AVPVideoPlayer), "PlayVideo"), new HarmonyMethod(drop));
            harmony.Patch(AccessTools.Method(typeof(BattleTech.UI.SGVideoPlayer), "PlayVideo"), new HarmonyMethod(drop));            
            */
            harmony.Patch(AccessTools.Method(typeof(BattleTech.IntroCinematicLauncher), "OnAddedToHierarchy"), new HarmonyMethod(AccessTools.Method(t, "IntroAdded")));


            Log("CDAL fix on");

            var resolver = AccessTools.Method(typeof(Resolver<ChassisDef>), "RequestDependencies"); //just using ChassisDef here to reference the static function. It means nothing;

            Assembly.GetAssembly(typeof(HeraldryDef))
                .GetTypes()
                .Where(ty => ty.GetInterface(typeof(DataManager.ILoadDependencies).FullName) != null)
                .ForEach(ildtype =>
                {
                    harmony.Patch(AccessTools.Method(ildtype, "CheckDependenciesAfterLoad"), Drop);
                    //harmony.Patch(AccessTools.Method(ildtype, "DependenciesLoaded"), Drop);
                    harmony.Patch(AccessTools.Method(ildtype, "RequestDependencies"), new HarmonyMethod(resolver));
                });

            harmony.Patch(AccessTools.Method(typeof(DataManager), "RequestResource_Internal"), new HarmonyMethod(AccessTools.Method(t, nameof(RequestResources_Internal2))));

            new ChassisDefResolver();
            new HeraldryDefResolver();
            new AbilityDefResolver();
            new BaseComponentRefResolver();
            new MechComponentRefResolver();
            new PilotDefResolver();
            new WeaponDefResolver();
            new MechDefResolver();
            new VehicleDefResolver();
        }
    
        public static void DataManager_Update(DataManager __instance, Dictionary<string, object> ___poolNextUpdate
            , Dictionary<BattleTechResourceType, Dictionary<string, object>> ___foregroundRequests
            , Dictionary<BattleTechResourceType, Dictionary<string, object>> ___backgroundRequests) {
            int f(Dictionary<BattleTechResourceType, Dictionary<string, object>> req)
                => req.SelectMany(r => r.Value).Count();
            Trap(() => Log("Pool, foreground, background: {0}, {1}, {2}", ___poolNextUpdate.Count, f(___foregroundRequests), f(___backgroundRequests)));
        }

        public static bool IntroAdded(IntroCinematicLauncher __instance)
        {
            return false;
        }

        static List<string> allfiles = new List<string>();
        public static void FileHookPath(string path)
        {
            allfiles.Add(path);
        }

        public static void FileHookName(string name)
        {
            allfiles.Add(name);
        }


        static Dictionary<string, object> resolveMap = new Dictionary<string, object>();

        class Resolver<T>
            where T : DataManager.ILoadDependencies
        {
            public Dictionary<T,IPromise> cache = new Dictionary<T,IPromise>();
            public Resolver()
            {
                if (resolveMap.ContainsKey(typeof(T).FullName)) LogWarning("Resolve map duplicate for {0}", typeof(T).FullName);
                else
                {
                    Log("Add resolver {0}", typeof(T).FullName);
                    resolveMap[typeof(T).FullName] = this;
                }
            }            

            internal virtual IPromise Resolve(T __instance, RT type, string id)
            {
                throw new System.Exception(string.Format("Missing Resolver<T>.Resolve for {0}", typeof(T).FullName));
            }

            public IPromise ResolveSafe(DataManager.ILoadDependencies ild, DataManager dataManager, RT type, string id)
            {
                if (ild == null)
                    LogError("ILD is null");
                if (dataManager == null)
                    LogError("DataManager is null");
                if (id == null)
                    LogError("id is null");
                if (ild.GetType() != typeof(T))
                    LogError("Resolve safe wrong ILD type");
                var __instance = (T)ild;
                __instance.DataManager = dataManager;
                var dummyload = new DummyLoadRequest(dataManager);
                new Traverse(__instance).Field("loadRequest").SetValue(dummyload);
                var idef = string.Format("{0}:{1}", id, Enum.GetName(typeof(RT), type));
                if (cache.TryGetValue(__instance, out var prom))
                {
                    LogDebug("ResolveSafe cached {0}", idef);
                    return prom;
                }
                else
                {
                    LogDebug("ResolveSafe {0}", idef);

                    var np = Trap(() => Resolve(__instance, type, id));
                    LogDebug("Cleanup {0}", idef);
                    if (ResolveDepsAsync.WantVerify)
                    {
                        np.Done(() =>
                        {

                            var dl = Trap(() => __instance.DependenciesLoaded(1000000));

                            if (dl) LogDebug("Resolved <?fixme T?> {0}", idef);
                            else
                            {
                                RequestDependencies_DryRun = true;
                                dryRun = new List<string>();
                                var lcopy = Trap(() =>
                                {
                                    if (__instance.DataManager == null)
                                        LogError("Can't find DM");
                                    __instance.RequestDependencies(__instance.DataManager, () => { }, dummyload); // Have to create a dummy request only for the stupid request weights.
                                    return string.Join(" ", dryRun.ToArray());
                                });
                                dryRun = null;
                                RequestDependencies_DryRun = false;

                                LogError("desynchronized {0} [{1}]", idef, lcopy);
                            }
                        });
                    }
                    cache[__instance] = np;
                    return np;
                }
            }

            
            static bool RequestDependencies_DryRun = false;
            /* This is the patch function which harmony calls for all ILoadDependencies types */
            public static bool RequestDependencies(DataManager.ILoadDependencies __instance, DataManager dataManager, Action onDependenciesLoaded, DataManager.DataManagerLoadRequest loadRequest)
            {
                // Something is running a dependency check, we want to ignore it.
                if (RequestDependencies_DryRun)
                {
                    LogDebug("Depcheck");
                    return true;
                }

                var t = __instance.GetType();
                if (resolveMap.TryGetValue(t.FullName, out var rescls))
                {
                    // we handle it
                    LogDebug("Resolver<T>.RequestDependencies where T = {0} && {1}", t.FullName, rescls.GetType().FullName);
                    var rs = rescls.GetType()
                        .GetMethod("ResolveSafe");
                    if (rs == null)
                        LogError("Unable to find ResolveSafe function");

                    var prom = (IPromise)rs.Invoke(rescls, new object[] { __instance, dataManager, loadRequest.ResourceType, loadRequest.ResourceId });
                    if (!onDependenciesLoaded.Method.DeclaringType.FullName.StartsWith("BattleTechPerformanceFix"))
                        prom.Done(onDependenciesLoaded);  // This is going to duplicate *a lot* of work.
                    return false;
                } else
                {
                    // DM handles it
                    LogDebug("Resolver<T>.RequestDependencies where T = {0} did not resolve and will pass through", t.FullName);
                    return true;
                }
            }
        }

        class ProxyResolver<T,K> : Resolver<T>
            where T : DataManager.ILoadDependencies
            where K : DataManager.ILoadDependencies
        {
            internal override IPromise Resolve(T __instance, RT type, string id)
            {
                var t = __instance.GetType();
                var k = typeof(K);
                if (resolveMap.TryGetValue(k.FullName, out var rescls))
                {
                    // we handle it
                    LogDebug("ResolverProxy<T->K>.Resolve where T = {0} K = {1} && {2}", t.FullName, k.FullName, rescls.GetType().FullName);
                    var rs = rescls.GetType()
                        .GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (rs == null)
                        LogError("Unable to find Proxied resolve function function");
                    LogDebug("Found proxied resolve");

                    return Trap(() => (IPromise)rs.Invoke(rescls, new object[] { __instance, type, id }));
                } else
                {
                    return Promise.Rejected(new Exception(string.Format("ResolverProxy<T->K>.Resolve FAILED where T = {0} K = {1} && {2}", t.FullName, k.FullName, rescls.GetType().FullName)));
                }
            }
        }

        class HeraldryDefResolver : Resolver<HeraldryDef>
        {
            internal override IPromise Resolve(HeraldryDef __instance, RT type, string id)
                => Promise.All(Load(RT.Texture2D, __instance.textureLogoID)
                                   , Promise.All(Sequence(__instance.primaryMechColorID, __instance.secondaryMechColorID, __instance.tertiaryMechColorID)
                                                     .Where(color => !string.IsNullOrEmpty(color))
                                                     .Select(color => Load(RT.ColorSwatch, color))));
        }

        class ChassisDefResolver : Resolver<ChassisDef>
        {
            internal override IPromise Resolve(ChassisDef __instance, RT type, string id)
            {
                LogDebug("Chassis-__instance: {0}", __instance == null ? "null" : "ok");
                LogDebug("Chassis-type: {0}", Enum.GetName(typeof(RT), type));
                LogDebug("Chassis-id: {0}", id);

                return Promise.All( __instance.FixedEquipment == null ? Promise.Resolved() : Promise.All(__instance.FixedEquipment.Select(equip => equip.Resolve(__instance.DataManager)))
                                  , __instance.FixedEquipment == null ? Promise.Resolved() : Promise.All(__instance.FixedEquipment.Where(equip => equip.Def != null && !string.IsNullOrEmpty(equip.prefabName)).Select(equip => Load(RT.Prefab, equip.prefabName)))
                                  , Load(RT.Prefab, __instance.PrefabIdentifier)
                                  , string.IsNullOrEmpty(__instance.Description.Icon) ? Promise.Resolved() : Load(RT.Sprite, __instance.Description.Icon)
                                  , Load(RT.HardpointDataDef, __instance.HardpointDataDefID)
                                  , Load(RT.MovementCapabilitiesDef, __instance.MovementCapDefID)
                                  , Load(RT.PathingCapabilitiesDef, __instance.PathingCapDefID))
                              .Then(() => __instance.Refresh());
            }
        }

        class AbilityDefResolver : Resolver<AbilityDef>
        {
            internal override IPromise Resolve(AbilityDef __instance, RT type, string id)
            {
                return Promise.All( string.IsNullOrEmpty(__instance.Description.Icon) ? Promise.Resolved() : Load(RT.SVGAsset, __instance.Description.Icon)
                                  , Promise.All(__instance.EffectData.Where(eff => !string.IsNullOrEmpty(eff.Description.Icon)).Select(eff => Load(RT.SVGAsset, eff.Description.Icon)))
                                  , string.IsNullOrEmpty(__instance.WeaponResource) ? Promise.Resolved() : Load(RT.WeaponDef, __instance.WeaponResource));
            }
        }

        class BaseComponentRefResolver : Resolver<BaseComponentRef>
        {
            internal override IPromise Resolve(BaseComponentRef __instance, RT type, string id)
            {
                return Load<MechComponentRef>(__instance.GetResourceType(), __instance.ComponentDefID)
                    .Then(def =>
                    {
                        new Traverse(__instance).Property("Def").SetValue(def);
                        __instance.Def.Resolve(__instance.DataManager);
                    });
            }
        }

        class PilotDefResolver : Resolver<PilotDef>
        {
            internal override IPromise Resolve(PilotDef __instance, RT type, string id)
            {
                //                                                                                                                             Add LoadAndResolve?
                return Promise.All(__instance.abilityDefNames == null ? Promise.Resolved() : Promise.All(__instance.abilityDefNames.Select(name => Load<AbilityDef>(RT.AbilityDef, name).Then(def => def.Resolve(__instance.DataManager))))
                                  , __instance.PortraitSettings == null ? Promise.Resolved() : Load(RT.PortraitSettings, __instance.PortraitSettings.Description.Id)
                                  , string.IsNullOrEmpty(__instance.Description.Id) ? Promise.Resolved() : Load(RT.Sprite, __instance.Description.Icon));
            }
        }

        class WeaponDefResolver : Resolver<WeaponDef>
        {
            internal override IPromise Resolve(WeaponDef __instance, RT type, string id)
            {
                return Promise.All(Load(RT.Prefab, __instance.WeaponEffectID)
                                  , string.IsNullOrEmpty(__instance.AmmoCategoryToAmmoId) ? Promise.Resolved() : Load(RT.AmmunitionDef, __instance.AmmoCategoryToAmmoId)
                                  , string.IsNullOrEmpty(__instance.AmmoCategoryToAmmoBoxId) ? Promise.Resolved() : Load(RT.AmmunitionBoxDef, __instance.AmmoCategoryToAmmoBoxId)
                                  , string.IsNullOrEmpty(__instance.Description.Icon) ? Promise.Resolved() : Load(RT.SVGAsset, __instance.Description.Icon)
                                  , Promise.All(__instance.statusEffects.Where(eff => !string.IsNullOrEmpty(eff.Description.Icon)).Select(eff => Load(RT.SVGAsset, eff.Description.Icon))));
            }
        }

        class MechDefResolver : Resolver<MechDef>
        {
            internal override IPromise Resolve(MechDef __instance, RT type, string id)
            {
                __instance.meleeWeaponRef.DataManager = __instance.dfaWeaponRef.DataManager = __instance.imaginaryLaserWeaponRef.DataManager = __instance.DataManager;
                return Promise.All(__instance.ChassisID.OfString(RT.ChassisDef)
                                  , __instance.Chassis == null ? Promise.Resolved() : __instance.Chassis.Resolve(__instance.DataManager)
                                  , __instance.HeraldryID.OfString(RT.HeraldryDef)
                                  , Promise.All(__instance.Inventory.Select(inv => inv.Resolve(__instance.DataManager)))
                                  , __instance.meleeWeaponRef.Resolve(__instance.DataManager)
                                  , __instance.dfaWeaponRef.Resolve(__instance.DataManager)
                                  , __instance.imaginaryLaserWeaponRef.Resolve(__instance.DataManager)
                                  , Promise.All(__instance.Inventory.Select(inv => inv.prefabName.OfString(RT.Prefab))));
            }
        }

        class VehicleDefResolver : Resolver<VehicleDef>
        {
            internal override IPromise Resolve(VehicleDef __instance, RT type, string id)
            {
                __instance.imaginaryLaserWeaponRef.DataManager = __instance.DataManager;
                return Promise.All(__instance.ChassisID.OfString(RT.ChassisDef)
                                  , __instance.Chassis == null ? Promise.Resolved() : __instance.Chassis.Resolve(__instance.DataManager)
                                  , __instance.HeraldryID.OfString(RT.HeraldryDef)
                                  , Promise.All(__instance.Inventory.Select(inv => inv.Resolve(__instance.DataManager)))
                                  , __instance.imaginaryLaserWeaponRef.Resolve(__instance.DataManager));
            }
        }

        class MechComponentRefResolver : ProxyResolver<MechComponentRef, BaseComponentRef> { }

        static Dictionary<string,int> track = new Dictionary<string,int>();

        public static void TrackRequestResource(DataManager __instance, BattleTechResourceType resourceType, PrewarmRequest prewarm)
        {
            var key = string.Format("{0}:{1}", Enum.GetName(typeof(RT), resourceType), prewarm != null);
            if (!track.ContainsKey(key)) track[key] = 0;
            track[key]++;
        }

        public static void IntegrityCheck(BattleTechResourceLocator __instance) {
            Trap(() => { 
                var manifest = new Traverse(__instance).Field("baseManifest").GetValue<Dictionary<BattleTechResourceType, Dictionary<string, VersionManifestEntry>>>();

                Control.Log("----------------- Manifest integrity check ---------------------------");
                var wrongIdents = manifest.SelectMany(type => type.Value.Where(entry => entry.Value.Id != entry.Key));
                var wrongTypes = manifest.SelectMany(type => type.Value.Where(entry => (RT)Enum.Parse(typeof(RT), entry.Value.Type) != type.Key));


                string f(VersionManifestEntry vme)
                    => string.Format("{0}:{1}", vme.Id, (RT)Enum.Parse(typeof(RT), vme.Type));


                Control.Log(string.Format("Wrong ids   ({0})", string.Join(" ", wrongIdents.Select(x => f(x.Value)).ToArray())));
                Control.Log(string.Format("Wrong types ({0})", string.Join(" ", wrongTypes.Select(x => f(x.Value)).ToArray())));

                manifest.SelectMany(types => types.Value.Select(entries => entries.Value))
                    .GroupBy(entry => entry.Id)
                    .Where(group => group.Count() > 1)
                    .ForEach(collision =>
                    {
                        string.Format("ID collision: ({0})", string.Join(" ", collision.Select(f).ToArray()));
                    });
            });
        }

        public static void TrackPre()
        {
            var frm = new StackFrame(1).GetMethod();
            var key = string.Format("{0}.{1}", frm.DeclaringType.Name, frm.Name);
            if (!track.ContainsKey(key)) track[key] = 0;
            if (!track.ContainsKey("total")) track["total"] = 0;
            track[key]++;
            track["total"]++;
        }

        public static void Summary()
        {
            Control.Log("(Track {0})", string.Join(" ", track.Select(kv => string.Format(":{0} {1}", kv.Key, kv.Value)).ToArray()));

            var counts = allfiles.GroupBy(s => s)
                .Where(g => g.Count() > 1)
                .Select(g => string.Format("{0}:{1}", g.First(), g.Count()));


            Control.Log("(File-duplicates {0})", string.Join(" ", counts.ToArray()));
            track.Clear();
        }

       
        public static Dictionary<string, Promise<object>> promises = new Dictionary<string, Promise<object>>();
        public static bool Initialized = false;
        public static Stuff stuff;

        public static Promise<object> Ensure(string id)
        {
            if (promises.TryGetValue(id, out var prom))
            {
                return prom;
            }
            else
            {
                var p = new Promise<object>();
                promises[id] = p;
                return p;
            }
        }

        public static IPromise Load(RT type, string id)
        {
            if (type == RT.MechDef)
            {
                return stuff.Load<MechDef>(type, id, false).Unit();
            }
            Trap(() => stuff.RequestResource(type, id, new PrewarmRequest(), false, false));
            return Ensure(id).Unit();
        }

        public static IPromise<T> Load<T>(RT type, string id)
        {
            Trap(() => stuff.RequestResource(type, id, new PrewarmRequest(), false, false));
            return Ensure(id)
                .Then(x => x is T ? (T)x : throw new Exception(string.Format("Load<T> Wanted {0}, but {1}:{2} is a {3}", typeof(T).FullName, id, Enum.GetName(typeof(RT), type), x.GetType().FullName)));
        }

        public static void DispatchAssetLoad(MessageCenterMessage msg)
        {
            var t = new Traverse(msg);
            var val = t.Property("Resource").GetValue();
            var id = t.Property("ResourceId").GetValue<string>();

            //FIXME: need to capture any multiple resolved promise here and do some cache correcting
            try
            {
                Ensure(id)
                    .Resolve(val);
            } catch
            {
                LogWarning("Asset {0} already dispatched", id);
            }
        }

        public static List<string> dryRun = null;
        public static bool RequestResources_Internal2(MethodInfo __originalMethod, DataManager __instance, BattleTechResourceType resourceType, string identifier, PrewarmRequest prewarm, bool allowRequestStacking, bool filterByOwnership)
        {
            if (dryRun != null)
            {
                dryRun.Add(string.Format("{0}:{1}", identifier, Enum.GetName(typeof(RT), resourceType)));
                return false;
            }
            LogDebug("Request {0}:{1}", identifier, Enum.GetName(typeof(RT), resourceType));
            if (!Initialized)
            {
                stuff = new Stuff(__instance);
                Initialized = true;
                stuff.messageCenter.AddSubscriber(MessageCenterMessageType.DataManagerRequestCompleteMessage, new ReceiveMessageCenterMessage(DispatchAssetLoad));
            }

            if (resourceType == RT.MechDef || resourceType == RT.Texture2D || resourceType == RT.SimGameConstants || resourceType == RT.BaseDescriptionDef || resourceType == RT.SimGameMilestoneDef || resourceType == RT.ShipModuleUpgrade || resourceType == RT.PortraitSettings)
            {
                Log("custom request: {0}", identifier);
                stuff.Load<object>(resourceType, identifier);
                return false;
            }

            return true;
        }

        public static bool CollectDeps = false;
        public static int CollectDepsDepth = 0;
        public static bool Halt = false;
        public static Dictionary<string, Promise<object>> reqCache = new Dictionary<string, Promise<object>>();
        public static bool RequestResources_Internal(MethodInfo __originalMethod, DataManager __instance, BattleTechResourceType resourceType, string identifier, PrewarmRequest prewarm, bool allowRequestStacking, bool filterByOwnership)
        {
            var stuff = new Stuff(__instance);
            
            // Just temporarily testing the waters here before writing the dependency functions
            if (resourceType == RT.Texture2D || resourceType == RT.SimGameConstants || resourceType == RT.BaseDescriptionDef || resourceType == RT.SimGameMilestoneDef || resourceType == RT.ShipModuleUpgrade || resourceType == RT.PortraitSettings)
            {
                LogDebug("Request {0} {1}", Enum.GetName(typeof(RT), resourceType), identifier);
                stuff.Load<object>(resourceType, identifier)
                     .Done(res =>
                     {
                         LogDebug("Loaded {0} {1}", Enum.GetName(typeof(RT), resourceType), identifier);
                     },
                     (ex) =>
                     {
                         LogException(ex);
                     });
                return false;
            }
            else
            {
                LogDebug("Unhandled {0} {1}", Enum.GetName(typeof(RT), resourceType), identifier);
            }
            return true;
        }
    }

    delegate void AcceptReject<T>(Action<T> accept, Action<Exception> reject);

    class Stuff
    {
        public DataManager dataManager;
        public HBS.Data.DataLoader dataLoader;
        public AssetBundleManager bundleManager;
        public MessageCenter messageCenter;
        public TextureManager textureManager;
        public Stuff(DataManager dataManager)
        {
            this.dataManager = dataManager;
            this.bundleManager = new Traverse(dataManager).Property("AssetBundleManager").GetValue<AssetBundleManager>();
            this.dataLoader = new Traverse(dataManager).Field("dataLoader").GetValue<HBS.Data.DataLoader>();
            this.messageCenter = new Traverse(dataManager).Property("MessageCenter").GetValue<MessageCenter>();
            this.textureManager = new Traverse(dataManager).Property("TextureManager").GetValue<TextureManager>();
        }

        public bool RequestResource(RT type, string id, PrewarmRequest p, bool stack, bool own)
        {
            return dataManager.RequestResource(type, id);
        }

        public void Add<T>(string field, string key, T item) where T : new()
        {
            Trap(() =>
            {
                new Traverse(dataManager)
                    .Field(field)
                    .GetValue<DictionaryStore<T>>()
                    .Add(key, item);
            });
        }
        
        public void LoadAndPublish<T>(RT resourceType, string identifier)
        {
            Load<T>(resourceType, identifier)
                .Done(res => messageCenter.PublishMessage(new DataManagerRequestCompleteMessage<T>(resourceType, identifier, res)));
        }

        public static Dictionary<string,object> cache = new Dictionary<string,object>();
        public IPromise<T> Load<T>(BattleTechResourceType resourceType, string identifier, bool publish = true)
        {
            // Store item in datamanager
            // It's likely necessary to clear the cache based on the dm field, since items may be removed from dm
            IPromise<T> f<K>(string field, IPromise<K> p)
                where K : new()
            {
                return p.Then(x =>
                {
                    Trap(() => new Traverse(dataManager).Field(field).GetValue<DictionaryStore<K>>().Add(identifier, x));
                    if(publish) messageCenter.PublishMessage(new DataManagerRequestCompleteMessage<K>(resourceType, identifier, x));
                    return (T)(object)x;
                });
            }

            IPromise<T> passthrough<K>(IPromise<K> p)
            {
                return p.Then(val =>
                {
                    if (publish) messageCenter.PublishMessage(new DataManagerRequestCompleteMessage<K>(resourceType, identifier, val));
                    return (T)(object)val;
                });
            }

            IPromise<T> Go()
            {
                LogDebug("Custom load: {0}:{1}", identifier, Enum.GetName(typeof(RT), resourceType));
                var entry = dataManager.ResourceLocator.EntryByID(identifier, resourceType, false);
                switch (resourceType)
                {
                    case RT.ChassisDef: return f("chassisDefs", LoadJson<ChassisDef>(entry, resourceType, identifier));
                    case RT.VehicleChassisDef: return f("vehicleChassisDefs", LoadJson<VehicleChassisDef>(entry, resourceType, identifier));
                    case RT.TurretChassisDef: return f("turretChassisDefs", LoadJson<TurretChassisDef>(entry, resourceType, identifier));
                    case RT.TurretDef: return f("turretDefs", LoadJson<TurretDef>(entry, resourceType, identifier));
                    case RT.BuildingDef: return f("buildingDefs", LoadJson<BuildingDef>(entry, resourceType, identifier));
                    case RT.AmmunitionDef: return f("ammoDefs", LoadJson<AmmunitionDef>(entry, resourceType, identifier));
                    case RT.AmmunitionBoxDef: return f("ammoBoxDefs", LoadJson<AmmunitionBoxDef>(entry, resourceType, identifier));
                    case RT.JumpJetDef: return f("jumpJetDefs", LoadJson<JumpJetDef>(entry, resourceType, identifier));
                    case RT.HeatSinkDef: return f("heatSinkDefs", LoadJson<HeatSinkDef>(entry, resourceType, identifier));
                    case RT.UpgradeDef: return f("upgradeDefs", LoadJson<UpgradeDef>(entry, resourceType, identifier));
                    case RT.WeaponDef: return f("weaponDefs", LoadJson<WeaponDef>(entry, resourceType, identifier));
                    case RT.MechDef: return f("mechDefs", LoadJson<MechDef>(entry, resourceType, identifier));
                    case RT.VehicleDef: return f("vehicleDefs", LoadJson<VehicleDef>(entry, resourceType, identifier));
                    case RT.PilotDef: return f("pilotDefs", LoadJson<PilotDef>(entry, resourceType, identifier));
                    case RT.AbilityDef: return f("abilityDefs", LoadJson<AbilityDef>(entry, resourceType, identifier));
                    case RT.DesignMaskDef: return f("designMaskDefs", LoadJson<DesignMaskDef>(entry, resourceType, identifier));
                    case RT.MovementCapabilitiesDef: return f("movementCapDefs", LoadJson<MovementCapabilitiesDef>(entry, resourceType, identifier));
                    case RT.PathingCapabilitiesDef: return f("pathingCapDefs", LoadJson<PathingCapabilitiesDef>(entry, resourceType, identifier));
                    case RT.HardpointDataDef: return f("hardpointDataDefs", LoadJson<HardpointDataDef>(entry, resourceType, identifier));
                    case RT.LanceDef: return f("lanceDefs", LoadJson<LanceDef>(entry, resourceType, identifier));
                    case RT.CastDef: return f("castDefs", LoadJson<CastDef>(entry, resourceType, identifier));
                    case RT.ConversationContent: return f("conversationDefs", LoadJson<ConversationContent>(entry, resourceType, identifier));
                    case RT.DialogBucketDef: return f("dialogBucketDefs", LoadJson<DialogBucketDef>(entry, resourceType, identifier));
                    case RT.SimGameEventDef: return f("simGameEventDefs", LoadJson<SimGameEventDef>(entry, resourceType, identifier));
                    case RT.SimGameStatDescDef: return f("simGameStatDescDefs", LoadJson<SimGameStatDescDef>(entry, resourceType, identifier));
                    case RT.LifepathNodeDef: return f("lifepathNodeDefs", LoadJson<LifepathNodeDef>(entry, resourceType, identifier));
                    //case RT.SimGameStringList: return f("simGameStringLists", LoadJson<SimGameStringList>(entry, resourceType, identifier));
                    case RT.ContractOverride: return f("contractOverrides", LoadJson<ContractOverride>(entry, resourceType, identifier));
                    case RT.StarSystemDef: return f("systemDefs", LoadJson<StarSystemDef>(entry, resourceType, identifier));
                    case RT.ShopDef: return f("shops", LoadJson<ShopDef>(entry, resourceType, identifier));
                    case RT.MechLabIncludeDef: return f("mechLabIncludeDefs", LoadJson<MechLabIncludeDef>(entry, resourceType, identifier));
                    case RT.FactionDef: return f("factions", LoadJson<FactionDef>(entry, resourceType, identifier));
                    case RT.HeraldryDef: return f("heraldries", LoadJson<HeraldryDef>(entry, resourceType, identifier));
                    //case RT.Conversation: return f("simGameConversations", LoadJson<Conversation>(entry, resourceType, identifier));
                    //case RT.ConversationSpeakerList: return f("simGameSpeakers", LoadJson<ConversationSpeakerList>(entry, resourceType, identifier));
                    case RT.GenderedOptionsListDef: return f("genderedOptionsListDefs", LoadJson<GenderedOptionsListDef>(entry, resourceType, identifier));
                    case RT.AudioEventDef: return f("audioEventDefs", LoadJson<AudioEventDef>(entry, resourceType, identifier));
                    case RT.SimGameMilestoneDef: return f("simGameMilestones", LoadJson<SimGameMilestoneDef>(entry, resourceType, identifier));
                    case RT.BackgroundDef: return f("backgroundDefs", LoadJson<BackgroundDef>(entry, resourceType, identifier));
                    case RT.BackgroundQuestionDef: return f("backgroundQuestionDefs", LoadJson<BackgroundQuestionDef>(entry, resourceType, identifier));
                    case RT.ShipModuleUpgrade: return f("shipUpgradeDefs", LoadJson<ShipModuleUpgrade>(entry, resourceType, identifier));
                    case RT.SimGameSubstitutionListDef: return f("simGameSubstitutionDefLists", LoadJson<SimGameSubstitutionListDef>(entry, resourceType, identifier));
                    case RT.BaseDescriptionDef: return f("baseDescriptionDefs", LoadJson<BaseDescriptionDef>(entry, resourceType, identifier));
                    case RT.PortraitSettings: return f("portraitSettings", LoadJson<PortraitSettings>(entry, resourceType, identifier));
                    case RT.SimGameDifficultySettingList: return f("simGameDifficultySettingLists", LoadJson<SimGameDifficultySettingList>(entry, resourceType, identifier));
                    case RT.FlashpointDef: return f("flashpointDefs", LoadJson<FlashpointDef>(entry, resourceType, identifier));
                    case RT.SimGameMilestoneSet: return f("milestoneSets", LoadJson<SimGameMilestoneSet>(entry, resourceType, identifier));
                    //case RT.ItemCollectionDef: return f("itemCollectionDefs", LoadCSV<ItemCollectionDef>(entry, resourceType, identifier));
                    //case RT.SimpleText: return f("simpleTexts", LoadJson<SimpleText>(entry, resourceType, identifier));

                    /*
                    case RT.Prefab: return LoadMapper(entry, resourceType, identifier, null, (GameObject go) => go);
                    case RT.Sprite:
                    case RT.SVGAsset:
                    case RT.ColorSwatch:
                    */
                    case RT.Texture2D:
                        /* if (this.TextureManager != null && this.TextureManager.Contains(identifier))
                            {
                                obj = this.TextureManager.GetLoadedTexture(identifier);
                            }
                            */
                        return passthrough(LoadMapper(entry, resourceType, identifier
                                                     , null
                                                     , (Texture2D t) => { textureManager.InsertTexture(identifier, t); return t; }
                                                     , (yes, no) => textureManager.RequestTexture(identifier, new TextureLoaded(yes), new LoadFailed(err => no(new Exception(err))))));

                /*
                case RT.UIModulePrefabs:
                case RT.AssetBundle: 
                */

                    // Grouped
                    case RT.BehaviorVariableScope:
                    case RT.ApplicationConstants:
                    case RT.AudioConstants:
                    case RT.CombatGameConstants:
                    case RT.MechStatisticsConstants:
                    case RT.SimGameConstants: return passthrough(LoadMapper(entry, resourceType, identifier, s => s, (TextAsset t) => t.text).Then(x => Promise<string>.Resolved(x)));
                }
                return Promise<T>.Rejected(new Exception(string.Format("Unhandled RT type {0}", Enum.GetName(typeof(RT), resourceType))));
            }

            if (cache.TryGetValue(identifier, out var promise)) return (Promise<T>)promise;
            else
            {
                // FIXME: This caching does not seem to be working, test it.
                LogDebug("Loading {0} {1}", Enum.GetName(typeof(RT), resourceType), identifier);
                var v = Go();
                cache[identifier] = v;
                return v;
            }
        }

        public IPromise<T> LoadJson<T>(VersionManifestEntry entry, BattleTechResourceType resourceType, string identifier)
            where T : class, HBS.Util.IJsonTemplated
        {
            T Make(string json)
            {
                T a = Activator.CreateInstance<T>();
                a.FromJSON(json);
                //Log("JSON send");
                return a;
            }

            return LoadMapper(entry, resourceType, identifier, Make, (TextAsset r) => Make(r.text));
        }
        
        // TODO: Looks like resource and bundle are the same type always, if so reduce them into one selector
        public IPromise<T> LoadMapper<T, R>(VersionManifestEntry entry, BattleTechResourceType resourceType, string identifier, Func<string, T> file, Func<R, T> resource, AcceptReject<T> recover = null) 
            where R : UnityEngine.Object 
        {
            var res = new Promise<T>();
            try
            {
                if (entry.IsFileAsset && file != null) dataLoader.LoadResource(entry.FilePath, c => res.Resolve(file(c)));
                else if (entry.IsResourcesAsset && resource != null) res.Resolve(resource(Resources.Load<R>(entry.ResourcesLoadPath)));
                else if (entry.IsAssetBundled && resource != null) bundleManager.RequestAsset<R>(resourceType, identifier, b => res.Resolve(resource(b)));
                else if (recover != null) recover(res.Resolve, res.Reject);
                else throw new System.Exception(string.Format("Unhandled file, resource, or asset", identifier));
            } catch (Exception e)
            {
                res.Reject(e);
            }
            return res;
        }

        public DataManager.DataManagerLoadRequest CreateRequest(BattleTechResourceType resourceType, string identifier)
        {
            if (dataManager == null)
                LogError("DM null & CreateRequest");
            var meth = AccessTools.Method(typeof(DataManager), "CreateByResourceType");
            if (meth == null)
                LogError("DM missing CreateResourceByType");
            return Trap(() => (DataManager.DataManagerLoadRequest)meth.Invoke(dataManager, new object[] { resourceType, identifier, new PrewarmRequest() }));
        }

        public static Sprite SpriteFromDisk(string assetPath)
        {
            if (!File.Exists(assetPath))
            {
                return null;
            }
            Sprite result;
            try
            {
                byte[] array = File.ReadAllBytes(assetPath);
                Texture2D texture2D;
                if (TextureManager.IsDDS(array))
                {
                    texture2D = TextureManager.LoadTextureDXT(array);
                }
                else
                {
                    if (!TextureManager.IsPNG(array) && !TextureManager.IsJPG(array))
                    {
                        LogWarning(string.Format("Unable to load unknown file type from disk (not DDS, PNG, or JPG) at: {0}", assetPath));
                        return null;
                    }
                    texture2D = new Texture2D(2, 2, TextureFormat.DXT5, false);
                    if (!texture2D.LoadImage(array))
                    {
                        return null;
                    }
                }
                result = Sprite.Create(texture2D, new UnityEngine.Rect(0f, 0f, (float)texture2D.width, (float)texture2D.height), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, Vector4.zero);
            }
            catch (Exception ex)
            {
                LogError(string.Format("Unable to load image at: {0}\nExceptionMessage:\n{1}", assetPath, ex.Message));
                result = null;
            }
            return result;
        }
    }
}