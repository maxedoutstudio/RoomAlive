using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using SharpDX;
using System.Globalization;

namespace RoomAliveToolkit
{
    public class Mesh
    {
        public struct VertexPositionNormalTexture
        {
            public Vector4 position;
            public Vector2 texture;
            public Vector3 normal;
            public const int sizeInBytes = 9 * 4;
        }

        public class Subset
        {
            public int start, length;
            public Material material;
        }

        public class Material
        {
            public Vector3 ambientColor;
            public Vector3 diffuseColor;
            public Vector3 specularColor;
            public float shininess;
            public string textureFilename;
        }

        private static float CULLING_CUTOFF = 10.0f;

        public List<VertexPositionNormalTexture> vertices = new List<VertexPositionNormalTexture>();
        public List<Subset> subsets = new List<Subset>();
        public Dictionary<string, Material> materials = new Dictionary<string, Material>();

        public static Mesh FromOBJFile(string filename)
        {
            var mesh = new Mesh();
            mesh.LoadFromOBJFile(filename);
            // Checks if there are normals
            if (mesh.vertices[0].normal.IsZero)
                mesh.CalculateNormals();
            // Does some basic cullling
            foreach(VertexPositionNormalTexture vpnt in mesh.vertices)
            {
                if (vpnt.position.Z < CULLING_CUTOFF)
                    mesh.vertices.Remove(vpnt);
            }
            return mesh;
        }

        void CalculateNormals()
        {
            for (int i = 0; i < vertices.Count; i += 3)
            {
                Vector3 a = new Vector3(vertices[i].position.X, vertices[i].position.Y, vertices[i].position.Z);
                Vector3 b = new Vector3(vertices[i+1].position.X, vertices[i + 1].position.Y, vertices[i + 1].position.Z);
                Vector3 c = new Vector3(vertices[i+2].position.X, vertices[i + 2].position.Y, vertices[i + 2].position.Z);

                Vector3 ab = b - a;
                Vector3 ac = c - a;

                Vector3 ba = -ab;
                Vector3 bc = c - b;

                Vector3 cb = -bc;
                Vector3 ca = -ac;

                Vector3 normalA = SharpDX.Vector3.Cross(ab, ac);
                Vector3 normalB = SharpDX.Vector3.Cross(ba, bc);
                Vector3 normalC = SharpDX.Vector3.Cross(cb, ca);

                if (normalA.Y < 0)
                    normalA = -normalA;

                if (normalB.Y < 0)
                    normalB = -normalB;

                if (normalC.Y < 0)
                    normalC = -normalC;

                normalA.Normalize();
                normalB.Normalize();
                normalC.Normalize();

                var vertexA = vertices[i];
                var vertexB = vertices[i+1];
                var vertexC = vertices[i+2];

                vertexA.normal = normalA;
                vertexB.normal = normalB;
                vertexC.normal = normalC;

                vertices[i] = vertexA;
                vertices[i+1] = vertexB;
                vertices[i+2] = vertexC;
            }
        }   

        void LoadFromOBJFile(string filename)
        {
            var file = new StreamReader(filename);
            var directory = Path.GetDirectoryName(filename);

            var positions = new List<Vector4>();
            var textureCoords = new List<Vector2>();
            var normals = new List<Vector3>();

            Subset subset = null;

            while (true)
            {
                string nextLine = file.ReadLine();
                if (nextLine == null) // end of file
                    break;
                var terms = nextLine.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (terms.Length == 0) // empty line
                    continue;
                var command = terms[0];
                if (command == "#" || command == "")
                {
                    // comment
                }
                else if (command == "v") // position
                {
                    float x = float.Parse(terms[1], CultureInfo.InvariantCulture);
                    float y = float.Parse(terms[2], CultureInfo.InvariantCulture);
                    float z = float.Parse(terms[3], CultureInfo.InvariantCulture);
                    positions.Add(new Vector4(x, y, z, 1));
                }
                else if (command == "vt") // texture coord
                {
                    float u = float.Parse(terms[1], CultureInfo.InvariantCulture);
                    float v = float.Parse(terms[2], CultureInfo.InvariantCulture);
                    textureCoords.Add(new Vector2(u, -v));
                }
                else if (command == "vn") // normal
                {
                    float x = float.Parse(terms[1], CultureInfo.InvariantCulture);
                    float y = float.Parse(terms[2], CultureInfo.InvariantCulture);
                    float z = float.Parse(terms[3], CultureInfo.InvariantCulture);
                    normals.Add(new Vector3(x, y, z));
                }
                else if (command == "f") // face
                {
                    for (int i = 0; i < 3; i++)
                    {
                        // TODO: suppoprt relative (negative) indices
                        var indices = terms[1 + i].Split('/');
                        var vertex = new VertexPositionNormalTexture();
                        vertex.position = positions[int.Parse(indices[0]) - 1]; // OBJ indices are 1-based    
                        if (indices[1] != "") // optional texture coords
                            vertex.texture = textureCoords[int.Parse(indices[1]) - 1];
                        if (indices.Length == 3)
                        {
                            // We need to calculate the normals if there aren't any later
                            if (indices[2] != "") // optional normal
                                vertex.normal = normals[int.Parse(indices[2]) - 1];
                        }
                        vertices.Add(vertex);
                        subset.length++;
                    }
                }
                else if (command == "mtllib") // .mtl file reference
                {
                    LoadMTLFile(directory + "/" + terms[1]);
                }
                else if (command == "usemtl") // material
                {
                    var name = terms[1];
                    Material material = null;
                    if (materials.ContainsKey(name))
                        material = materials[name];
                    else 
                    {
                        material = new Material();
                        materials.Add(name, material);
                    }
                    subset = new Subset();
                    subset.material = material;
                    subset.start = vertices.Count; // next vertex to be created
                    subsets.Add(subset);
                }
                else
                {
                    // unimplemented or unrecognized command
                }
            }
            file.Close();
        }

        void LoadMTLFile(string filename)
        {
            var file = new StreamReader(filename);
            var directory = Path.GetDirectoryName(filename);

            Material material = null;
            while (true)
            {
                string nextLine = file.ReadLine();
                if (nextLine == null)
                    break;
                var terms = nextLine.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (terms.Length == 0)
                    continue;
                string command = terms[0];
                if (command == "newmtl")
                {
                    var name = terms[1];
                    if (materials.ContainsKey(name))
                        material = materials[name];
                    else
                    {
                        material = new Material();
                        materials.Add(name, material);
                    }
                }
                else if (command == "#") // comment
                {
                }
                else if (command == "Ka") // ambient color
                {
                    material.ambientColor.X = float.Parse(terms[1], CultureInfo.InvariantCulture);
                    material.ambientColor.Y = float.Parse(terms[2], CultureInfo.InvariantCulture);
                    material.ambientColor.Z = float.Parse(terms[3], CultureInfo.InvariantCulture);
                }
                else if (command == "Kd") // diffuse color
                {
                    material.diffuseColor.X = float.Parse(terms[1], CultureInfo.InvariantCulture);
                    material.diffuseColor.Y = float.Parse(terms[2], CultureInfo.InvariantCulture);
                    material.diffuseColor.Z = float.Parse(terms[3], CultureInfo.InvariantCulture);
                }
                else if (command == "Ks") // specular color
                {
                    material.specularColor.X = float.Parse(terms[1], CultureInfo.InvariantCulture);
                    material.specularColor.Y = float.Parse(terms[2], CultureInfo.InvariantCulture);
                    material.specularColor.Z = float.Parse(terms[3], CultureInfo.InvariantCulture);
                }
                else if ((command == "d") || (command == "Tr"))
                {
                    //material.alpha = float.Parse(terms[1]);
                }
                else if (command == "Ns")
                {
                    material.shininess = (int)float.Parse(terms[1], CultureInfo.InvariantCulture);
                }
                else if (command == "illum")
                {
                    //material.specular = int.Parse(terms[1]) == 2;
                }
                else if (command == "map_Kd")
                {
                    material.textureFilename = directory + "/" + terms[1];
                }
                else
                {
                    // Unimplemented or unrecognized command
                }
            }

            file.Close();
        }    
    }
}
