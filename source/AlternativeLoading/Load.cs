﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RSG;
using BattleTech;
using UnityEngine;
using BattleTech.Data;
using static BattletechPerformanceFix.Extensions;

// NOTE: Bundles & Resources that are async loaded seem to be force-able

namespace BattletechPerformanceFix.AlternativeLoading
{
    public delegate void AcceptReject<T>(Action<T> accept, Action<Exception> reject);
    public static class Load
    {
        public static IPromise<T> MapSync<T,R>( VersionManifestEntry entry
                                              , Func<byte[],T> byFile
                                              , Func<R,T> byResource 
                                              , Func<R,T> byBundle
                                              , AcceptReject<T> recover = null) where R : UnityEngine.Object {
            try { if (entry == null) throw new Exception("MapSync: null entry");
                  if (entry.IsAssetBundled && byBundle != null) return LoadAssetFromBundle<R>(entry.Id, entry.AssetBundleName).Then(byBundle);
                  if (entry.IsResourcesAsset && byResource != null) return Promise<T>.Resolved(byResource(Resources.Load<R>(entry.ResourcesLoadPath)));
                  if (entry.IsFileAsset && byFile != null) return Promise<T>.Resolved(byFile(File.ReadAllBytes(entry.FilePath))); }
            catch(Exception e) { return Promise<T>.Rejected(e); }
            return Promise<T>.Rejected(new Exception($"Missing method to load {entry.Id}"));
        }


        public static IPromise<T> LoadAssetFromBundle<T>(string id, string bundleName) {
            return Promise<T>.Rejected(new Exception("NYI: LoadAssetFromBundle"));
        }

        public static IPromise<T> LoadJson<T>( VersionManifestEntry entry) where T : HBS.Util.IJsonTemplated {
            return MapSync( entry
                          , System.Text.Encoding.UTF8.GetString
                          , (TextAsset t) => t.text
                          , (TextAsset t) => t.text)
                .Then(str => { var inst = Activator.CreateInstance<T>().NullThrowError($"No Activator for {typeof(T).FullName}");
                               inst.FromJSON(str);
                               return inst; });

        }

        public static IPromise<T> DMResolveDependencies<T>(this IPromise<T> p) {
            return p.Then(maybeDeps => {
                    if (maybeDeps is DataManager.ILoadDependencies) {
                        var ild = maybeDeps as DataManager.ILoadDependencies;
                        var prom = new Promise<T>();
                        // FIXME: This will require the DummyLoader from rda branch
                        ild.RequestDependencies(DMGlue.DM, () => prom.Resolve(maybeDeps), null);
                        return prom;
                    } else {
                        return Promise<T>.Resolved(maybeDeps);
                    }
                });
        }
    }
}
