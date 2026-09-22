using System.Collections.Generic;
using UnityEngine;

// Dynamic settling is shape-independent. Sleep the connected contact/joint group together:
// sleeping one body while its neighbours remain awake repeatedly restarts the solver and
// can build large contact impulses in a quiet mixed Kinematic/Dynamic stack.
public partial class BlockController
{
    private static readonly List<Rigidbody2D> SleepGroupBodies = new List<Rigidbody2D>(64);
    private static readonly HashSet<Rigidbody2D> SleepGroupVisited = new HashSet<Rigidbody2D>();
    private static readonly List<BlockController> SleepGroupBlocks = new List<BlockController>(64);
    private static readonly List<Joint2D> SleepGroupJoints = new List<Joint2D>(16);
    private static readonly List<Joint2D> SleepJointScratch = new List<Joint2D>(8);

    private void TrySleepSettledGroup()
    {
        try
        {
            // Vine/Maw joints live on either endpoint, and often disable collisions.
            // Collect both directions rather than relying on contacts or this body's joints.
            for (int i = 0; i < TrackedBlocks.Count; i++)
            {
                BlockController block = TrackedBlocks[i];
                if (block == null) continue;
                block.GetComponents(SleepJointScratch);
                for (int j = 0; j < SleepJointScratch.Count; j++)
                {
                    Joint2D joint = SleepJointScratch[j];
                    if (joint != null && joint.isActiveAndEnabled) SleepGroupJoints.Add(joint);
                }
            }

            AddSleepGroupBody(_rb);
            for (int i = 0; i < SleepGroupBodies.Count; i++)
            {
                Rigidbody2D body = SleepGroupBodies[i];
                if (!body.simulated || body.sleepMode == RigidbodySleepMode2D.NeverSleep ||
                    !body.TryGetComponent(out BlockController block)) return;

                if (body.bodyType == RigidbodyType2D.Kinematic)
                {
                    // Only grid-owned, motionless anchors participate. An active piece or
                    // externally controlled moving body must never be stopped by settling.
                    if (!block.HasLanded || !block.IsGridStable || block._isControlEnabled ||
                        body.linearVelocity.sqrMagnitude > 0f || body.angularVelocity != 0f) return;
                }
                else
                {
                    if (!block.IsReadyForGroupSleep()) return;
                    SleepGroupBlocks.Add(block);
                }

                // Reusable lists grow to fit dense compound contacts; truncating a fixed
                // buffer could miss the one moving neighbour that must veto sleep.
                int count = body.GetContacts(SharedContactBuffer);
                for (int j = 0; j < count; j++)
                {
                    ContactPoint2D contact = SharedContactBuffer[j];
                    AddSleepGroupBody(contact.rigidbody);
                    AddSleepGroupBody(contact.otherRigidbody);
                }

                for (int j = 0; j < SleepGroupJoints.Count; j++)
                {
                    Joint2D joint = SleepGroupJoints[j];
                    if (joint.attachedRigidbody == body) AddSleepGroupBody(joint.connectedBody);
                    else if (joint.connectedBody == body) AddSleepGroupBody(joint.attachedRigidbody);
                }
            }

            // Velocity writes can wake neighbours. Finish ALL writes before sleeping ANY
            // body, including motionless Kinematic anchors that can otherwise wake the group.
            for (int i = 0; i < SleepGroupBodies.Count; i++)
            {
                SleepGroupBodies[i].linearVelocity = Vector2.zero;
                SleepGroupBodies[i].angularVelocity = 0f;
            }
            for (int i = 0; i < SleepGroupBodies.Count; i++) SleepGroupBodies[i].Sleep();
            for (int i = 0; i < SleepGroupBlocks.Count; i++)
            {
                BlockController block = SleepGroupBlocks[i];
                block.ResetSettlingTimers();
                block._fallingAway = false;
            }
        }
        finally
        {
            SleepGroupBodies.Clear();
            SleepGroupVisited.Clear();
            SleepGroupBlocks.Clear();
            SleepGroupJoints.Clear();
            SleepJointScratch.Clear();
            SharedContactBuffer.Clear();
        }
    }

    private static void AddSleepGroupBody(Rigidbody2D body)
    {
        // Static terrain terminates a contact branch. It does not wake neighbours and must
        // not couple unrelated piles just because they share the same floor.
        if (body == null || body.bodyType == RigidbodyType2D.Static) return;
        if (SleepGroupVisited.Add(body)) SleepGroupBodies.Add(body);
    }
}
