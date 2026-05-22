using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ImmersiveNPCs.Tests
{
    /// <summary>
    /// Test harness for the tiered context pipeline.
    /// Uses stubbed providers to run without actual models.
    /// </summary>
    [TestFixture]
    public class TieredContextPipelineTests
    {
        private AIConversationSettings testSettings;
        
        [SetUp]
        public void Setup()
        {
            testSettings = UnityEngine.ScriptableObject.CreateInstance<AIConversationSettings>();
            testSettings.enableTieredContext = true;
            testSettings.qualityPreset = QualityPreset.Balanced;
            testSettings.enableWorldStateValidation = true;
            testSettings.maxLineLength = 280;
            testSettings.maxOptionLength = 120;
        }
        
        [TearDown]
        public void Teardown()
        {
            if (testSettings != null)
            {
                UnityEngine.Object.DestroyImmediate(testSettings);
            }
        }
        
        // === Repetition Detection Tests ===
        
        [Test]
        public void RepetitionDetection_ExactMatch_ReturnsTrue()
        {
            string newLine = "The fog is thick today. Be careful out there.";
            var recentTurns = new List<AIConversationTurn>
            {
                new AIConversationTurn { npcLine = "The fog is thick today. Be careful out there.", playerChoice = "Go to the market" }
            };
            
            bool isRepeat = AIOutputValidator.IsRepeatedResponse(newLine, recentTurns);
            
            Assert.IsTrue(isRepeat, "Exact match should be detected as repetition");
        }
        
        [Test]
        public void RepetitionDetection_SimilarText_ReturnsTrue()
        {
            string newLine = "The fog is very thick today. Be careful!";
            var recentTurns = new List<AIConversationTurn>
            {
                new AIConversationTurn { npcLine = "The fog is thick today. Be careful out there.", playerChoice = "Go to the market" }
            };
            
            bool isRepeat = AIOutputValidator.IsRepeatedResponse(newLine, recentTurns);
            
            Assert.IsTrue(isRepeat, "Similar text should be detected as repetition");
        }
        
        [Test]
        public void RepetitionDetection_DifferentText_ReturnsFalse()
        {
            string newLine = "Welcome to my shop! What would you like to buy?";
            var recentTurns = new List<AIConversationTurn>
            {
                new AIConversationTurn { npcLine = "The fog is thick today. Be careful out there.", playerChoice = "Go to the market" }
            };
            
            bool isRepeat = AIOutputValidator.IsRepeatedResponse(newLine, recentTurns);
            
            Assert.IsFalse(isRepeat, "Different text should not be detected as repetition");
        }
        
        // === World State Validation Tests ===
        
        [Test]
        public void QuestFlagMismatch_InvalidLocation_Detected()
        {
            var validator = new ResponseValidator
            {
                Strictness = ResponseValidator.StrictnessLevel.Moderate
            };
            
            var snapshot = new WorldStateSnapshot();
            snapshot.validLocations.Add("Market");
            snapshot.validLocations.Add("Harbor");
            snapshot.validLocations.Add("Tavern");
            
            string npcLine = "You should go to the Ancient Temple to find the artifact.";
            
            var result = validator.Validate(npcLine, snapshot, null);
            
            Assert.IsFalse(result.isValid, "Invalid location should be detected");
            Assert.Greater(result.violations.Count, 0, "Should have at least one violation");
        }
        
        [Test]
        public void QuestFlagMismatch_ValidLocation_Passes()
        {
            var validator = new ResponseValidator
            {
                Strictness = ResponseValidator.StrictnessLevel.Moderate
            };
            
            var snapshot = new WorldStateSnapshot();
            snapshot.validLocations.Add("Market");
            snapshot.validLocations.Add("Harbor");
            snapshot.validLocations.Add("Tavern");
            
            string npcLine = "You should go to the Market to find supplies.";
            
            var result = validator.Validate(npcLine, snapshot, null);
            
            Assert.IsTrue(result.isValid, "Valid location should pass validation");
        }
        
        // === Script Authority Tests ===
        
        [Test]
        public void ScriptAuthority_MainQuestBeat_UsesScriptedResponse()
        {
            var arbiter = new ScriptAuthorityArbiter();
            arbiter.RegisterRequiredResponse("quest_start", "Welcome, traveler. I have a task for you.");
            
            var snapshot = new WorldStateSnapshot
            {
                isScriptedBeat = true,
                currentQuestBeat = "quest_start"
            };
            
            string llmResponse = "Hello there! Nice weather we're having.";
            
            var result = arbiter.Arbitrate(llmResponse, snapshot, null, null);
            
            Assert.AreEqual(ScriptAuthorityArbiter.Decision.UseScriptedResponse, result.decision);
            Assert.AreEqual("Welcome, traveler. I have a task for you.", result.modifiedResponse);
        }
        
        [Test]
        public void ScriptAuthority_SideChatter_UsesLlmResponse()
        {
            var arbiter = new ScriptAuthorityArbiter();
            
            var snapshot = new WorldStateSnapshot
            {
                isScriptedBeat = false,
                currentQuestBeat = ""
            };
            
            string llmResponse = "Hello there! Nice weather we're having.";
            
            var result = arbiter.Arbitrate(llmResponse, snapshot, null, null);
            
            Assert.AreEqual(ScriptAuthorityArbiter.Decision.UseLlmResponse, result.decision);
            Assert.AreEqual(llmResponse, result.modifiedResponse);
        }
        
        [Test]
        public void ScriptAuthority_YarnNodeTag_DetectsMainQuest()
        {
            var arbiter = new ScriptAuthorityArbiter();
            
            var snapshot = new WorldStateSnapshot();
            snapshot.yarnNodeTags.Add("main_quest");
            snapshot.yarnNodeTags.Add("act1");
            
            bool isMainQuest = arbiter.IsMainQuestBeat(snapshot);
            
            Assert.IsTrue(isMainQuest, "main_quest tag should indicate main quest beat");
        }
        
        // === Memory Write Gating Tests ===
        
        [Test]
        public void MemoryWriteGating_PlayerName_IsCommitWorthy()
        {
            var policy = new MemoryWritePolicy();
            
            string playerChoice = "My name is Marcus.";
            string npcLine = "Nice to meet you, Marcus!";
            
            var events = policy.AnalyzeTurn("npc_1", playerChoice, npcLine, null);
            
            Assert.Greater(events.Count, 0, "Player name should generate memory event");
            Assert.IsTrue(events.Exists(e => e.eventType == MemoryEventType.PlayerNameRevealed));
        }
        
        [Test]
        public void MemoryWriteGating_SmallTalk_IsNotCommitWorthy()
        {
            var policy = new MemoryWritePolicy
            {
                MinImportanceThreshold = 0.5f
            };
            
            string playerChoice = "Nice weather today.";
            string npcLine = "Indeed it is!";
            
            var events = policy.AnalyzeTurn("npc_1", playerChoice, npcLine, null);
            
            // Small talk shouldn't generate commit-worthy events
            int commitWorthy = 0;
            foreach (var evt in events)
            {
                if (policy.ShouldCommit(evt))
                {
                    commitWorthy++;
                }
            }
            
            Assert.AreEqual(0, commitWorthy, "Small talk should not be commit-worthy");
        }
        
        [Test]
        public void MemoryWriteGating_Promise_IsCommitWorthy()
        {
            var policy = new MemoryWritePolicy();
            
            string playerChoice = "I promise I'll help you.";
            string npcLine = "Thank you, I'll hold you to that.";
            
            var events = policy.AnalyzeTurn("npc_1", playerChoice, npcLine, null);
            
            Assert.Greater(events.Count, 0, "Promise should generate memory event");
            Assert.IsTrue(events.Exists(e => e.eventType == MemoryEventType.PromiseMade));
        }
        
        // === Policy Resolver Tests ===
        
        [Test]
        public void PolicyResolver_FastSmall_DisablesPlanning()
        {
            var resolver = new PolicyResolver(testSettings);
            
            var policy = resolver.Resolve(QualityPreset.FastSmall, 2048);
            
            Assert.IsFalse(policy.enablePlanning, "FastSmall should disable planning");
            Assert.IsFalse(policy.enableMemoryWrites, "FastSmall should disable memory writes");
            Assert.AreEqual(ResponseValidator.StrictnessLevel.Lenient, policy.validationStrictness);
        }
        
        [Test]
        public void PolicyResolver_CinematicQuality_MaximizesBudgets()
        {
            var resolver = new PolicyResolver(testSettings);
            
            var policy = resolver.Resolve(QualityPreset.CinematicQuality, 8192);
            
            Assert.IsTrue(policy.enablePlanning, "CinematicQuality should enable planning");
            Assert.IsTrue(policy.enableMemoryWrites, "CinematicQuality should enable memory writes");
            Assert.IsTrue(policy.enableStreaming, "CinematicQuality should enable streaming");
            Assert.AreEqual(ResponseValidator.StrictnessLevel.Strict, policy.validationStrictness);
        }
        
        // === Context Tier Budgets Tests ===
        
        [Test]
        public void ContextTierBudgets_ScalesWithContextWindow()
        {
            var smallBudgets = ContextTierBudgets.CreateDefault(2048);
            var largeBudgets = ContextTierBudgets.CreateDefault(8192);
            
            Assert.Greater(largeBudgets.tierBIdentity, smallBudgets.tierBIdentity, "Larger context should have larger identity budget");
            Assert.Greater(largeBudgets.tierCMemory, smallBudgets.tierCMemory, "Larger context should have larger memory budget");
        }
        
        // === Intent Plan Tests ===
        
        [Test]
        public void IntentPlan_FallbackFromQuestion_DetectsQuestion()
        {
            var snapshot = new WorldStateSnapshot();
            
            string playerChoice = "Where is the market?";
            var plan = IntentPlan.CreateFallback(playerChoice, snapshot);
            
            Assert.AreEqual(IntentType.AnswerQuestion, plan.intent, "Question should be detected");
            Assert.IsTrue(plan.isFallback, "Should be marked as fallback");
        }
        
        [Test]
        public void IntentPlan_FallbackFromCombat_DetectsCombat()
        {
            var snapshot = new WorldStateSnapshot
            {
                inCombat = true
            };
            
            string playerChoice = "Attack!";
            var plan = IntentPlan.CreateFallback(playerChoice, snapshot);
            
            Assert.AreEqual(IntentType.CombatBark, plan.intent, "Combat should be detected");
        }
        
        [Test]
        public void IntentPlan_FallbackFromVendor_DetectsVendor()
        {
            var snapshot = new WorldStateSnapshot
            {
                isVendorMode = true
            };
            
            string playerChoice = "What do you have for sale?";
            var plan = IntentPlan.CreateFallback(playerChoice, snapshot);
            
            Assert.AreEqual(IntentType.VendorTrade, plan.intent, "Vendor mode should be detected");
        }
        
        // === Snapshot Builder Tests ===
        
        [Test]
        public void SnapshotBuilder_BuildsCompleteSnapshot()
        {
            var snapshot = new SnapshotBuilder()
                .Begin()
                .SetLocation("Harbor")
                .SetTimeOfDay("Evening")
                .SetEmotionalTone("tense")
                .SetActiveQuest("find_artifact")
                .SetQuestBeat("talk_to_captain")
                .AddFlag("met_captain")
                .AddValidLocation("Harbor")
                .AddValidLocation("Ship")
                .AddKnownNpc("Captain Morgan")
                .SetRelationship(25, "friendly")
                .Build();
            
            Assert.AreEqual("Harbor", snapshot.currentLocation);
            Assert.AreEqual("Evening", snapshot.timeOfDay);
            Assert.AreEqual("tense", snapshot.emotionalTone);
            Assert.AreEqual("find_artifact", snapshot.activeQuestId);
            Assert.IsTrue(snapshot.HasFlag("met_captain"));
            Assert.IsTrue(snapshot.IsValidLocation("Harbor"));
            Assert.IsTrue(snapshot.IsKnownNpc("Captain Morgan"));
            Assert.AreEqual(25, snapshot.relationshipLevel);
            Assert.AreEqual("friendly", snapshot.relationshipTier);
        }
        
        // === Memory Store Tests ===
        
        [Test]
        public void MemoryStore_RetrievesPersistentFacts()
        {
            var store = new MemoryStore();
            
            var playerName = MemoryEvent.PlayerName("npc_1", "Marcus");
            var smallTalk = MemoryEvent.Create(MemoryEventType.Custom, "npc_1", "Weather is nice.");
            smallTalk.isPersistentFact = false;
            
            store.Add(playerName);
            store.Add(smallTalk);
            
            var facts = store.GetPersistentFacts("npc_1");
            
            Assert.AreEqual(1, facts.Count, "Should only return persistent facts");
            Assert.AreEqual(MemoryEventType.PlayerNameRevealed, facts[0].eventType);
        }
        
        [Test]
        public void MemoryStore_EvictsLowImportance()
        {
            var store = new MemoryStore();
            store.Configure(maxPerNpc: 3, maxGlobal: 10);
            
            // Add 5 events with varying importance
            var evt1 = MemoryEvent.Create(MemoryEventType.Custom, "npc_1", "Low 1");
            evt1.importance = 0.1f;
            store.Add(evt1);
            
            var evt2 = MemoryEvent.Create(MemoryEventType.Custom, "npc_1", "Low 2");
            evt2.importance = 0.2f;
            store.Add(evt2);
            
            var evt3 = MemoryEvent.Create(MemoryEventType.Custom, "npc_1", "High 1");
            evt3.importance = 0.9f;
            store.Add(evt3);
            
            var evt4 = MemoryEvent.Create(MemoryEventType.Custom, "npc_1", "High 2");
            evt4.importance = 0.8f;
            store.Add(evt4);
            
            var evt5 = MemoryEvent.Create(MemoryEventType.Custom, "npc_1", "High 3");
            evt5.importance = 0.7f;
            store.Add(evt5);
            
            var events = store.GetAllEventsForNpc("npc_1");
            
            Assert.AreEqual(3, events.Count, "Should evict to max capacity");
            
            // Low importance should be evicted
            Assert.IsFalse(events.Exists(e => e.summary == "Low 1"));
            Assert.IsFalse(events.Exists(e => e.summary == "Low 2"));
        }
    }
    
    /// <summary>
    /// Stub provider for testing without actual model.
    /// </summary>
    public class StubAIProvider : IAIProvider
    {
        public string NextResponse { get; set; } = "{\"npc_line\":\"Test response.\",\"options\":[\"Option 1\",\"Option 2\",\"Option 3\",\"Option 4\"],\"mood\":\"neutral\"}";
        public int CallCount { get; private set; }
        
        public Task<TurnResult> GenerateTurnAsync(AIContext context, CancellationToken ct)
        {
            CallCount++;
            
            if (AIOutputValidator.TryParse(NextResponse, out TurnResult result))
            {
                result.metadata = new ProviderMetadata
                {
                    providerName = "Stub",
                    latencyMs = 10
                };
                return Task.FromResult(result);
            }
            
            return Task.FromResult(AIOutputValidator.CreateFallback(context.slots, null));
        }
    }
}
