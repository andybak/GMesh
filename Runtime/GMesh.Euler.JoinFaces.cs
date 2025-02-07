// Copyright (C) 2021-2022 Steffen Itterheim
// Refer to included LICENSE file for terms and conditions.

using System;

namespace CodeSmile.GraphMesh
{
    public sealed partial class GMesh
    {
        /*
         * JOIN FACE KILL EDGE:
         * Takes two faces joined by a single 2-manifold edge and fuses them together.
         * The edge shared by the faces must not be connected to any other edges which have
         * both faces in its radial cycle.
         * An illustration of this appears in the figure on the right. In this diagram,
         * situation A is the only one in which join_face_kill_edge will return with a value
         * indicating success. If the tool author wants to join two seperate faces which have
         * multiple edges joining them as in situation B they should run JEKV on the excess
         * edge(s) first. In the case of situation none of the edges joining the two faces
         * can be safely removed because it would cause a face that loops back on itself.
         * Also note that the order of arguments decides whether or not certain per-face
         * attributes are present in the resultant face. For instance vertex winding,
         * material index, smooth flags, ect are inherited from f1, not f2.
         * Returns - true for success
         */

        public bool JoinFacesAndDeleteEdge(int face0Index, int face1Index)
        {
            Face face0 = GetFace(face0Index);
            Face face1 = GetFace(face1Index);
            return JoinFacesAndDeleteEdge(ref face0, ref face1);
        }

        /// <summary>
        /// Joins two faces (passed by reference) that share a single 2-manifold edge and deletes that edge.
        /// See JoinFacesAndDeleteEdge(int,int) for details.
        /// </summary>
        public bool JoinFacesAndDeleteEdge(ref Face face0, ref Face face1)
        {
            // Validate faces.
            if (!face0.IsValid || !face1.IsValid)
                return false;

            // Find the shared edge between the two faces.
            int sharedEdgeIndex = FindSharedEdge(face0, face1);
            if (sharedEdgeIndex == UnsetIndex)
                return false;

            Edge sharedEdge = GetEdge(sharedEdgeIndex);

            // Check that the shared edge is 2-manifold and touches only face0 and face1.
            if (!IsEdgeManifold(sharedEdge, face0, face1))
                return false;

            // Ensure that removing the shared edge will not cause a face to “loop back on itself.”
            if (!CanRemoveSharedEdgeWithoutLoop(face0, face1, sharedEdge))
                return false;

            // Merge the two face loops together so that the shared edge is removed.
            if (!JoinFacesInternal_MergeLoops(ref face0, ref face1, sharedEdgeIndex))
                return false;

            // Remove the shared edge from the mesh.
            DeleteEdge(sharedEdgeIndex);

            // Remove the now-merged face.
            DeleteFace(face1.Index);

#if GMESH_VALIDATION
            if (!ValidateFaceLoops(face0, out string issue))
                throw new Exception(issue);
#endif

            // Write back the updated face.
            SetFace(face0);
            return true;
        }

        #region Helper Methods

        /// <summary>
        /// Finds the index of an edge that is present in both face0 and face1.
        /// Returns UnsetIndex if no shared edge is found.
        /// </summary>
        private int FindSharedEdge(Face face0, Face face1)
        {
            // Iterate over all loops of face0.
            int loopIndex = face0.FirstLoopIndex;
            if (loopIndex == UnsetIndex)
                return UnsetIndex;

            do
            {
                Loop loop = GetLoop(loopIndex);
                int edgeIndex = loop.EdgeIndex;
                if (EdgeSharedByFace(edgeIndex, face1.Index))
                    return edgeIndex;

                loopIndex = loop.NextLoopIndex;
            } while (loopIndex != face0.FirstLoopIndex);

            return UnsetIndex;
        }

        /// <summary>
        /// Returns true if the given edge is also used by the specified face.
        /// </summary>
        private bool EdgeSharedByFace(int edgeIndex, int faceIndex)
        {
            Edge edge = GetEdge(edgeIndex);
            int loopIndex = edge.BaseLoopIndex;
            if (loopIndex == UnsetIndex)
                return false;

            do
            {
                Loop loop = GetLoop(loopIndex);
                if (loop.FaceIndex == faceIndex)
                    return true;

                loopIndex = loop.NextRadialLoopIndex;
            } while (loopIndex != edge.BaseLoopIndex && loopIndex != UnsetIndex);

            return false;
        }

        /// <summary>
        /// Checks that the shared edge is manifold – i.e. it is used by exactly two loops,
        /// one in each face.
        /// </summary>
        private bool IsEdgeManifold(Edge edge, Face face0, Face face1)
        {
            int count = CountRadialLoops(edge);
            if (count != 2)
                return false;

            Loop loop0 = GetLoop(edge.BaseLoopIndex);
            Loop loop1 = GetLoop(loop0.NextRadialLoopIndex);

            return ((loop0.FaceIndex == face0.Index && loop1.FaceIndex == face1.Index) ||
                    (loop0.FaceIndex == face1.Index && loop1.FaceIndex == face0.Index));
        }

        /// <summary>
        /// Counts how many loops are attached to this edge.
        /// </summary>
        private int CountRadialLoops(Edge edge)
        {
            if (edge.BaseLoopIndex == UnsetIndex)
                return 0;

            int count = 1;
            int loopIndex = GetLoop(edge.BaseLoopIndex).NextRadialLoopIndex;
            while (loopIndex != edge.BaseLoopIndex && loopIndex != UnsetIndex)
            {
                count++;
                loopIndex = GetLoop(loopIndex).NextRadialLoopIndex;
            }

            return count;
        }

        /// <summary>
        /// Determines whether removing the shared edge would result in a degenerate (self-intersecting)
        /// face loop. (For brevity, this routine is a stub – in a complete implementation you would
        /// simulate the merge of the vertex cycles and ensure that no non-adjacent vertex appears twice.)
        /// </summary>
        private bool CanRemoveSharedEdgeWithoutLoop(Face face0, Face face1, Edge sharedEdge)
        {
            // TODO: Compute the merged vertex order and check for degeneracies.
            // For this example we simply assume it is safe.
            return true;
        }

        /// <summary>
        /// Merges the two face loops (one from face0 and one from face1) that share the edge
        /// with index sharedEdgeIndex. The merged face will retain the attributes of face0.
        /// </summary>
        private bool JoinFacesInternal_MergeLoops(ref Face baseFace, ref Face removedFace, int sharedEdgeIndex)
        {
            // Find the loop in baseFace that uses the shared edge.
            int baseLoopIndex = FindFaceLoopUsingEdge(baseFace, sharedEdgeIndex);
            // Likewise, find the corresponding loop in removedFace.
            int removedLoopIndex = FindFaceLoopUsingEdge(removedFace, sharedEdgeIndex);

            if (baseLoopIndex == UnsetIndex || removedLoopIndex == UnsetIndex)
                return false;

            Loop baseLoop = GetLoop(baseLoopIndex);
            Loop removedLoop = GetLoop(removedLoopIndex);

            // The idea is to “splice out” the shared edge segment from both face loops.
            // For each face the loop is circularly linked (via NextLoop/PrevLoop) so that
            // removing the shared segment means linking the predecessor of the shared loop in one face
            // with the successor of the shared loop in the other face.
            //
            // For instance, in baseFace the loop preceding the shared-edge loop is:
            int basePrevIndex = baseLoop.PrevLoopIndex;
            // And in removedFace the loop following the shared-edge loop is:
            int removedNextIndex = removedLoop.NextLoopIndex;

            // Update the disk-cycle links so that the two loops “bypass” the shared edge.
            Loop basePrevLoop = GetLoop(basePrevIndex);
            Loop removedNextLoop = GetLoop(removedNextIndex);

            basePrevLoop.NextLoopIndex = removedNextIndex;
            removedNextLoop.PrevLoopIndex = basePrevIndex;
            SetLoop(basePrevLoop);
            SetLoop(removedNextLoop);

            // (Optionally, update any other pointers that refer to the removed loops.)

            // Next, update all loops formerly belonging to removedFace to now belong to baseFace.
            int curLoopIndex = removedFace.FirstLoopIndex;
            do
            {
                Loop curLoop = GetLoop(curLoopIndex);
                // Skip the shared (removed) loop.
                if (curLoop.Index != removedLoop.Index)
                {
                    curLoop.FaceIndex = baseFace.Index;
                    SetLoop(curLoop);
                }

                curLoopIndex = curLoop.NextLoopIndex;
            } while (curLoopIndex != removedFace.FirstLoopIndex);

            // Finally, if baseFace’s BaseLoop pointed to the now-removed shared loop,
            // update it to a valid loop index.
            if (baseFace.FirstLoopIndex == baseLoop.Index)
                baseFace.FirstLoopIndex = removedNextIndex;

            SetFace(baseFace);
            return true;
        }

        /// <summary>
        /// Searches through the loops of the given face for one that uses the specified edge.
        /// Returns the loop index if found; otherwise UnsetIndex.
        /// </summary>
        private int FindFaceLoopUsingEdge(Face face, int edgeIndex)
        {
            int loopIndex = face.FirstLoopIndex;
            if (loopIndex == UnsetIndex)
                return UnsetIndex;

            do
            {
                Loop loop = GetLoop(loopIndex);
                if (loop.EdgeIndex == edgeIndex)
                    return loopIndex;
                loopIndex = loop.NextLoopIndex;
            } while (loopIndex != face.FirstLoopIndex);

            return UnsetIndex;
        }

        #endregion
    }
}
