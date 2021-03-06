﻿using Assets;
using Assets.Car;
using Assets.Fileparsers;
using Assets.Scripts.System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Assets.System
{
    class LevelLoader : MonoBehaviour
    {
        public Dictionary<string, GameObject> LevelObjects;
        public Material TerrainMaterial;
        public GameObject SpawnPrefab, RegenPrefab;

        public void LoadLevel(string msnFilename)
        {
            LevelObjects = new Dictionary<string, GameObject>();
            var cacheManager = FindObjectOfType<CacheManager>();

            var terrainPatches = new Terrain[80, 80];
            var mdef = MsnMissionParser.ReadMsnMission(msnFilename);

            cacheManager.Palette = ActPaletteParser.ReadActPalette(mdef.PaletteFilePath);
            var _surfaceTexture = TextureParser.ReadMapTexture(mdef.SurfaceTextureFilePath, cacheManager.Palette);
            _surfaceTexture.filterMode = FilterMode.Point;

            FindObjectOfType<Sky>().TextureFilename = mdef.SkyTextureFilePath;

            var worldGameObject = GameObject.Find("World");
            if (worldGameObject != null)
                Destroy(worldGameObject);
            worldGameObject = new GameObject("World");


            var splatPrototypes = new[]
            {
                new SplatPrototype
                {
                    texture = _surfaceTexture,
                    tileSize = new Vector2(_surfaceTexture.width, _surfaceTexture.height) / 10.0f,
                    metallic = 0,
                    smoothness = 0
                }
            };
            
            for (int z = 0; z < 80; z++)
            {
                for (int x = 0; x < 80; x++)
                {
                    if (mdef.TerrainPatches[x, z] == null)
                        continue;

                    var patchGameObject = new GameObject("Ter " + x + ", " + z);
                    patchGameObject.transform.position = new Vector3(x * 640, 0, z * 640);
                    patchGameObject.transform.parent = worldGameObject.transform;

                    var terrain = patchGameObject.AddComponent<Terrain>();
                    terrain.terrainData = mdef.TerrainPatches[x, z].TerrainData;
                    terrain.terrainData.splatPrototypes = splatPrototypes;
                    terrain.materialTemplate = TerrainMaterial;
                    terrain.materialType = Terrain.MaterialType.Custom;

                    var terrainCollider = patchGameObject.AddComponent<TerrainCollider>();
                    terrainCollider.terrainData = terrain.terrainData;

                    foreach (var odef in mdef.TerrainPatches[x, z].Objects)
                    {
                        GameObject go = null;
                        if (odef.ClassId == MsnMissionParser.ClassId.Car)
                        {
                            var lblUpper = odef.Label.ToUpper();
                            
                            switch (lblUpper)
                            {
                                case "SPAWN":
                                    go = Instantiate(SpawnPrefab);
                                    go.tag = "Spawn";
                                    break;
                                case "REGEN":
                                    go = Instantiate(RegenPrefab);
                                    go.tag = "Regen";
                                    break;
                                default:
                                    go = cacheManager.ImportVcf(odef.Label + ".vcf", odef.TeamId == 1);
                                    break;
                            }

                            go.transform.parent = patchGameObject.transform;
                            go.transform.localPosition = odef.LocalPosition;
                            go.transform.localRotation = odef.LocalRotation;
                        }
                        else if (odef.ClassId != MsnMissionParser.ClassId.Special)
                        {
                            go = cacheManager.ImportSdf(odef.Label + ".sdf", patchGameObject.transform, odef.LocalPosition, odef.LocalRotation);
                            if (odef.ClassId == MsnMissionParser.ClassId.Sign)
                            {
                                go.AddComponent<FlyOffOnImpact>();
                            }
                        }

                        if(go != null)
                        {
                            go.name = odef.Label + "_" + odef.Id;
                            LevelObjects.Add(go.name, go);
                        }
                    }

                    terrainPatches[x, z] = terrain;
                }
            }

            foreach (var road in mdef.Roads)
            {
                var roadGo = new GameObject("Road");
                roadGo.transform.parent = worldGameObject.transform;
                var meshCollider = roadGo.AddComponent<MeshCollider>();
                var meshFilter = roadGo.AddComponent<MeshFilter>();
                var meshRenderer = roadGo.AddComponent<MeshRenderer>();
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;


                string roadTextureFilename;
                switch (road.SegmentType)
                {
                    case MsnMissionParser.RoadSegmentType.PavedHighway:
                        roadTextureFilename = "r2ayr_51";
                        break;
                    case MsnMissionParser.RoadSegmentType.DirtTrack:
                        roadTextureFilename = "r2dnr_37";
                        break;
                    case MsnMissionParser.RoadSegmentType.RiverBed:
                        roadTextureFilename = "r2wnr_39";
                        break;
                    case MsnMissionParser.RoadSegmentType.FourLaneHighway:
                        roadTextureFilename = "r2ayr_51";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                var roadMaterial = cacheManager.GetTextureMaterial(roadTextureFilename, false);
                meshRenderer.material = roadMaterial;

                var mesh = new Mesh();
                var vertices = new List<Vector3>();
                var uvs = new List<Vector2>();

                var uvIdx = 0;
                foreach (var roadSegment in road.RoadSegments)
                {
                    vertices.Add(roadSegment.Left);
                    vertices.Add(roadSegment.Right);

                    uvs.Add(new Vector2(0, uvIdx));
                    uvs.Add(new Vector2(1, uvIdx));
                    uvIdx += 1;
                }

                var indices = new List<int>();
                var idx = 0;
                for (int i = 0; i < (vertices.Count - 2) / 2; i++)
                {
                    indices.Add(idx + 2);
                    indices.Add(idx + 1);
                    indices.Add(idx);

                    indices.Add(idx + 2);
                    indices.Add(idx + 3);
                    indices.Add(idx + 1);
                    idx += 2;
                }

                mesh.vertices = vertices.ToArray();
                mesh.triangles = indices.ToArray();
                mesh.uv = uvs.ToArray();
                mesh.RecalculateNormals();
                meshFilter.sharedMesh = mesh;
                meshCollider.sharedMesh = mesh;
            }

            foreach (var ldef in mdef.StringObjects)
            {
                var sdfObj = cacheManager.ImportSdf(ldef.Label + ".sdf", null, Vector3.zero, Quaternion.identity);

                for (int i = 0; i < ldef.StringPositions.Count; i++)
                {
                    var pos = ldef.StringPositions[i];
                    var localPosition = new Vector3(pos.x % 640, pos.y, pos.z % 640);
                    var patchPosX = (int)(pos.x / 640.0f);
                    var patchPosZ = (int)(pos.z / 640.0f);
                    sdfObj.name = ldef.Label + " " + i;
                    sdfObj.transform.parent = terrainPatches[patchPosX, patchPosZ].transform;
                    sdfObj.transform.localPosition = localPosition;
                    if (i < ldef.StringPositions.Count - 1)
                    {
                        sdfObj.transform.LookAt(ldef.StringPositions[i + 1], Vector3.up);
                    }
                    else
                    {
                        sdfObj.transform.LookAt(ldef.StringPositions[i - 1], Vector3.up);
                        sdfObj.transform.localRotation *= Quaternion.AngleAxis(180, Vector3.up);
                    }

                    if (i < ldef.StringPositions.Count - 1)
                        sdfObj = Instantiate(sdfObj);
                }
            }

            worldGameObject.transform.position = new Vector3(-mdef.Middle.x * 640, 0, -mdef.Middle.y * 640);


            FindObjectOfType<Light>().color = cacheManager.Palette[176];
            Camera.main.backgroundColor = cacheManager.Palette[239];
            RenderSettings.fogColor = cacheManager.Palette[239];
            RenderSettings.ambientLight = cacheManager.Palette[247];

            var cars = FindObjectsOfType<NewCar>();
            foreach (var car in cars)
            {
                car.transform.parent = null;
            }

            foreach (var machine in mdef.FSM.StackMachines)
            {
                machine.Reset();
                machine.Constants = new int[machine.InitialArguments.Length];
                for (int i = 0; i < machine.Constants.Length; i++)
                {
                    var stackValue = machine.InitialArguments[i];
                    machine.Constants[i] = mdef.FSM.Constants[stackValue];
                }
            }
            var fsmRunner = FindObjectOfType<FSMRunner>();
            fsmRunner.FSM = mdef.FSM;
        }
    }
}