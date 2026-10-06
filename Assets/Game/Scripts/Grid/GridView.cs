using UnityEngine;

namespace StoneSignal
{
    public sealed class GridView : MonoBehaviour
    {
        private GridManager grid;
        private PathfindingManager pathfinding;
        private LineRenderer route;
        private Mesh directionMesh;
        public void Initialize(GridManager map, PathfindingManager paths, VisualPalette palette)
        {
            grid = map; pathfinding = paths;
            for (int y = 0; y < grid.height; y++) for (int x = 0; x < grid.width; x++)
            {
                var p = new Vector2Int(x, y);
                Material mat = p == grid.spawn ? palette.spawn : p == grid.goal ? palette.goal : ((x + y) % 2 == 0 ? palette.tileA : palette.tileB);
                if(palette.art!=null) ArtVisual.Create((x+y)%2==0 ? palette.art.tileA : palette.art.tileB,transform,grid.ToWorld(p),grid.cellSize);
                else PrimitiveVisual.Create("Tile " + x + "," + y, PrimitiveType.Cube, transform, grid.ToWorld(p) - Vector3.up * .13f, new Vector3(.95f, .2f, .95f) * grid.cellSize, mat);
            }
            if(palette.art!=null) {
                ArtVisual.Create(palette.art.signalCore,transform,grid.ToWorld(grid.goal));
                ArtVisual.Create(palette.art.spawnPortal,transform,grid.ToWorld(grid.spawn));
                var environment=new GameObject("Tabletop decoration");environment.transform.SetParent(transform);
                environment.AddComponent<RuinEnvironment>().Initialize(grid,paths,palette.art);
            } else {
                PrimitiveVisual.Create("Signal core",PrimitiveType.Cylinder,transform,grid.ToWorld(grid.goal)+Vector3.up*.45f,new Vector3(.62f,.45f,.62f),palette.goal);
                PrimitiveVisual.Create("Spawn gate",PrimitiveType.Cylinder,transform,grid.ToWorld(grid.spawn)+Vector3.up*.35f,new Vector3(.7f,.35f,.7f),palette.spawn);
            }
            foreach(var endpoint in new[]{grid.spawn,grid.goal}) {
                var label=new GameObject(endpoint==grid.spawn?"SPAWN":"CORE"); label.transform.SetParent(transform); label.transform.position=grid.ToWorld(endpoint)+Vector3.up*1.65f;
                label.transform.rotation=Quaternion.Euler(48,34,0); var text=label.AddComponent<TextMesh>(); text.text=label.name; text.fontSize=36; text.characterSize=.055f; text.anchor=TextAnchor.MiddleCenter; text.color=new Color(1,.94f,.77f);
            }
            route = new GameObject("Current route").AddComponent<LineRenderer>();
            route.transform.SetParent(transform);
            route.sharedMaterial = palette.path;
            route.startWidth = route.endWidth = .11f;
            route.numCapVertices = 3;
            var directions=new GameObject("Route direction chevrons",typeof(MeshFilter),typeof(MeshRenderer));
            directions.transform.SetParent(transform,false);
            directionMesh=new Mesh {name="Current route arrows"};directions.GetComponent<MeshFilter>().sharedMesh=directionMesh;
            directions.GetComponent<MeshRenderer>().sharedMaterial=palette.path;
            pathfinding.PathChanged += DrawPath;
            DrawPath();
        }
        private void DrawPath()
        {
            route.positionCount = pathfinding.CurrentPath.Count;
            for (int i = 0; i < pathfinding.CurrentPath.Count; i++) route.SetPosition(i, grid.ToWorld(pathfinding.CurrentPath[i]) + Vector3.up * .03f);
            var vertices=new System.Collections.Generic.List<Vector3>();var triangles=new System.Collections.Generic.List<int>();
            for(int i=0;i<pathfinding.CurrentPath.Count-1;i+=2)
            {
                Vector3 a=grid.ToWorld(pathfinding.CurrentPath[i]), b=grid.ToWorld(pathfinding.CurrentPath[i+1]);
                Vector3 forward=(b-a).normalized, side=Vector3.Cross(Vector3.up,forward);
                Vector3 center=Vector3.Lerp(a,b,.55f)+Vector3.up*.05f;
                int index=vertices.Count;
                vertices.Add(transform.InverseTransformPoint(center+forward*.2f));
                vertices.Add(transform.InverseTransformPoint(center-forward*.13f-side*.15f));
                vertices.Add(transform.InverseTransformPoint(center-forward*.13f+side*.15f));
                triangles.Add(index);triangles.Add(index+2);triangles.Add(index+1);
            }
            directionMesh.Clear();directionMesh.SetVertices(vertices);directionMesh.SetTriangles(triangles,0);directionMesh.RecalculateNormals();
        }
        private void OnDestroy() { if (pathfinding != null) pathfinding.PathChanged -= DrawPath; if(directionMesh!=null) PrimitiveVisual.DestroyObject(directionMesh); }
    }
}

