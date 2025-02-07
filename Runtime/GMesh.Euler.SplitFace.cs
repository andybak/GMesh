// Copyright (C) 2021-2022 Steffen Itterheim
// Refer to included LICENSE file for terms and conditions.

using System;
using System.Collections.Generic;
using Unity.Collections;

namespace CodeSmile.GraphMesh
{
	public sealed partial class GMesh
	{
		/*
		 * SPLIT FACE MAKE EDGE:
		 * Takes as input two vertices in a single face. An edge is created which divides
		 * the original face into two distinct regions. One of the regions is assigned to
		 * the original face and it is closed off. The second region has a new face assigned to it.
		 * Note that if the input vertices share an edge this will create a face with only two edges.
		 * Returns - new Face and new Edge indices
		 */

		public (int newFaceIndex, int newEdgeIndex) SplitFaceAndCreateEdge(int faceIndex, int vertexAIndex,
			int vertexBIndex)
		{
			Face face = GetFace(faceIndex);
			if (!face.IsValid)
				return (UnsetIndex, UnsetIndex);

			// Get the face's vertex order.
			NativeArray<int> faceVertsArray = GetFaceVertexIndices(face);
			var faceVerts = new List<int>(faceVertsArray.ToArray());
			faceVertsArray.Dispose();

			int posA = faceVerts.IndexOf(vertexAIndex);
			int posB = faceVerts.IndexOf(vertexBIndex);
			if (posA < 0 || posB < 0)
				throw new Exception("Vertices not found in face");

			// Ensure posA comes before posB.
			if (posB < posA)
			{
				int temp = posA;
				posA = posB;
				posB = temp;
			}

			// Split the vertex order into two polygons.
			var poly1 = faceVerts.GetRange(posA, posB - posA + 1);
			var poly2 = new List<int>();
			poly2.AddRange(faceVerts.GetRange(posB, faceVerts.Count - posB));
			poly2.AddRange(faceVerts.GetRange(0, posA + 1));

			// Create the new edge connecting the two vertices.
			var newEdge = Edge.Create(vertexAIndex, vertexBIndex);
			AddEdge(ref newEdge);

			// Update the original face with poly1.
			UpdateFace(face.Index, new NativeArray<int>(poly1.ToArray(), Allocator.Temp));

			// Create a new face with poly2.
			int newFaceIndex = CreateFace(new NativeArray<int>(poly2.ToArray(), Allocator.Temp));
			Face newFace = GetFace(newFaceIndex);

			// Attach the new edge to both faces.
			AttachEdgeToFace(face.Index, newEdge.Index);
			AttachEdgeToFace(newFace.Index, newEdge.Index);

#if GMESH_VALIDATION
		if (ValidateFace(face, out var issue1) == false) throw new Exception(issue1);
		if (ValidateFace(newFace, out var issue2) == false) throw new Exception(issue2);
#endif

			return (newFace.Index, newEdge.Index);
		}

		private NativeArray<int> GetFaceVertexIndices(Face face)
		{
			List<int> indices = new List<int>();
			int startLoopIndex = face.FirstLoopIndex;
			int currentLoopIndex = startLoopIndex;
			do
			{
				Loop loop = GetLoop(currentLoopIndex);
				indices.Add(loop.StartVertexIndex);
				currentLoopIndex = loop.NextLoopIndex;
			} while (currentLoopIndex != startLoopIndex);

			return new NativeArray<int>(indices.ToArray(), Allocator.Temp);
		}

		private void UpdateFace(int faceIndex, NativeArray<int> vertexIndices)
		{
			// Rebuild the face's loop from the given vertex order.
			// Placeholder implementation: rebuild the face via CreateFace and transfer its index.
			int tempFaceIndex = CreateFace(vertexIndices);
			Face tempFace = GetFace(tempFaceIndex);
			tempFace.Index = faceIndex;
			SetFace(tempFace);
			// Optionally remove the temporary face record if needed.
		}

		private void AttachEdgeToFace(int faceIndex, int edgeIndex)
		{
			// Attach the edge to the face's loop if not already present.
			Face face = GetFace(faceIndex);
			Loop loop = GetLoop(face.FirstLoopIndex);
			if (loop.EdgeIndex != edgeIndex)
			{
				loop.EdgeIndex = edgeIndex;
				SetLoop(loop);
			}
		}
	}
}
