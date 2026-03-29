namespace CBRE.Providers.Map
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using CBRE.DataStructures.GameData;
    using CBRE.DataStructures.Geometric;
    using CBRE.DataStructures.MapObjects;
    using CBRE.Providers.Texture;
    using Path = System.IO.Path;

    public class RMeshProvider : MapProvider
    {
        protected override bool IsValidForFileName(string filename)
        {
            return Path.GetExtension(filename) == ".rmesh";
        }

        protected override Map GetFromStream(Stream stream, IEnumerable<string> modelDirs, out Image[] lightmaps)
        {
            lightmaps = null;

            BinaryReader reader = new BinaryReader(stream);

            string verifyString = reader.ReadB3DString();
            bool hasTriggerBox = false;

            switch (verifyString)
            {
                case "RoomMesh":
                    break;
                case "RoomMesh.HasTriggerBox":
                    hasTriggerBox = true;
                    break;
                default:
                    throw new ProviderException("RMesh file is corrupted/invalid!");
            }

            Map map = new Map();
            map.WorldSpawn = new World(map.IDGenerator.GetNextObjectID());

            int meshCount = reader.ReadInt32();

            for (int i = 0; i < meshCount; i++)
            {
                ReadSolids(reader, map.IDGenerator, map.WorldSpawn);
            }

            // TODO Hidden polys, entities...

            reader.Close();

            return map;
        }

        protected override void SaveToStream(Stream stream, Map map, GameData gameData, TextureCollection textureCollection)
        {
            throw new InvalidOperationException();
        }

        protected override IEnumerable<MapFeature> GetFormatFeatures()
        {
            return new[]
            {
                MapFeature.Entities,
                MapFeature.Solids,
            };
        }

        public override void PostLoad(Map map, TextureCollection textureCollection)
        {
            foreach (Solid solid in map.WorldSpawn.GetChildren().OfType<Solid>())
            {
                foreach (Face face in solid.Faces)
                {
                    TextureItem texture = textureCollection.GetItem(face.Texture.Name);

                    if (texture == null)
                    {
                        continue;
                    }

                    SetFaceTextureUvs(face, texture.Width, texture.Height);
                }
            }
        }

        private Solid[] ReadSolids(BinaryReader reader, IDGenerator generator, World world)
        {
            byte flag = reader.ReadByte();

            if (flag != 0)
            {
                reader.ReadB3DString();
            }

            reader.ReadByte();

            string textureName = Path.GetFileNameWithoutExtension(reader.ReadB3DString());

            int numVertices = reader.ReadInt32();

            Vertex[] vertices = new Vertex[numVertices];

            for (int i = 0; i < numVertices; i++)
            {
                decimal x = (decimal)reader.ReadSingle();
                decimal z = (decimal)reader.ReadSingle();
                decimal y = (decimal)reader.ReadSingle();

                Vertex vertex = new Vertex(new Coordinate(x, y, z), null);

                float u = reader.ReadSingle();
                float v = reader.ReadSingle();

                vertex.DTextureU = u;
                vertex.DTextureV = v;

                reader.ReadSingle();
                reader.ReadSingle();

                reader.ReadByte();
                reader.ReadByte();
                reader.ReadByte();

                vertices[i] = vertex;
            }

            int numTriangles = reader.ReadInt32();

            Solid[] solids = new Solid[numTriangles];

            for (int i = 0; i < numTriangles; i++)
            {
                Solid solid = new Solid(generator.GetNextObjectID());
                Face face = new Face(generator.GetNextFaceID());

                int indexA = reader.ReadInt32();
                int indexB = reader.ReadInt32();
                int indexC = reader.ReadInt32();

                Vertex vertexA = vertices[indexA].Clone();
                Vertex vertexB = vertices[indexB].Clone();
                Vertex vertexC = vertices[indexC].Clone();

                vertexA.Parent = face;
                vertexB.Parent = face;
                vertexC.Parent = face;

                face.Texture.Name = textureName;

                face.Vertices = new List<Vertex>
                {
                    vertices[indexA],
                    vertices[indexB],
                    vertices[indexC],
                };

                face.Plane = new Plane(face.Vertices[0].Location, face.Vertices[1].Location, face.Vertices[2].Location);

                face.Parent = solid;

                face.UpdateBoundingBox();

                solid.Faces.Add(face);
                solid.SetParent(world, false);
                solid.UpdateBoundingBox(false);
            }

            return solids;
        }

        private static bool SetFaceTextureUvs(Face face, int textureWidth, int textureHeight)
        {
            if (face.Vertices.Count < 3) return false;
            if (textureWidth == 0 || textureHeight == 0) return false;

            Vertex v0 = face.Vertices[0];
            Vertex v1 = face.Vertices[1];
            Vertex v2 = face.Vertices[2];

            Coordinate dP1 = v1.Location - v0.Location;
            Coordinate dP2 = v2.Location - v0.Location;

            decimal dU1 = (decimal)(v1.DTextureU - v0.DTextureU);
            decimal dU2 = (decimal)(v2.DTextureU - v0.DTextureU);
            decimal dV1 = (decimal)(v1.DTextureV - v0.DTextureV);
            decimal dV2 = (decimal)(v2.DTextureV - v0.DTextureV);

            decimal a = dP1.Dot(dP1);
            decimal b = dP1.Dot(dP2);
            decimal c = dP2.Dot(dP2);

            decimal detA = a * c - b * b;

            if (Math.Abs(detA) < 1e-10m)
            {
                return false;
            }

            Coordinate vT = (dP1 * (c * dU1 - b * dU2) + dP2 * (a * dU2 - b * dU1)) / detA;
            Coordinate vB = (dP1 * (c * dV1 - b * dV2) + dP2 * (a * dV2 - b * dV1)) / detA;

            decimal tLen = vT.VectorMagnitude();
            decimal bLen = vB.VectorMagnitude();

            if (tLen < 1e-10m || bLen < 1e-10m)
            {
                return false;
            }

            face.Texture.UAxis  = vT / tLen;
            face.Texture.VAxis  = vB / bLen;
            face.Texture.XScale = 1.0m / (tLen * textureWidth);
            face.Texture.YScale = 1.0m / (bLen * textureHeight);

            decimal uAdd = (decimal)v0.DTextureU - v0.Location.Dot(vT);
            decimal vAdd = (decimal)v0.DTextureV - v0.Location.Dot(vB);
            face.Texture.XShift = uAdd * textureWidth;
            face.Texture.YShift = vAdd * textureHeight;

            return true;
        }
    }
}