using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Operators;

namespace TrashTD.Tests
{
    public class PanaceaTests
    {
        private readonly List<Object> created = new List<Object>();
        private GridManager gridManager;
        private int nextColumn;

        [SetUp]
        public void SetUp()
        {
            var stageData = ScriptableObject.CreateInstance<StageData>();
            stageData.gridWidth = 8;
            stageData.gridHeight = 1;
            stageData.tileLayout = new TileType[8];
            for (int i = 0; i < 8; i++) stageData.tileLayout[i] = TileType.HighGround;
            created.Add(stageData);

            var gridObject = new GameObject("TestGridManager");
            created.Add(gridObject);
            gridManager = gridObject.AddComponent<GridManager>();
            gridManager.InitializeFromStageData(stageData);
            nextColumn = 0;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in created)
                if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
        }

        /// <summary>A deployed Medic with the given max HP, set to the given current HP.</summary>
        private OperatorBase Deploy<T>(int maxHp, int currentHp) where T : OperatorBase
        {
            var data = ScriptableObject.CreateInstance<OperatorData>();
            data.operatorName = "Test" + nextColumn;
            data.operatorClass = OperatorClass.Medic;
            data.position = OperatorPosition.Ranged;
            data.baseRarity = OperatorRarity.Star1;
            data.baseHP = maxHp;
            data.baseATK = 46;
            data.attackInterval = 5f;
            created.Add(data);

            var obj = new GameObject(data.operatorName);
            obj.AddComponent<SpriteRenderer>();
            created.Add(obj);
            T op = obj.AddComponent<T>();
            op.Initialize(data, OperatorRarity.Star1);
            Assert.IsTrue(op.Deploy(gridManager.GetCell(new Vector2Int(nextColumn++, 0))));
            if (currentHp < op.MaxHP) op.RestoreSavedHP(currentHp);
            return op;
        }

        [Test]
        public void PicksTheLowestHpPercent_NotTheLowestHp()
        {
            OperatorBase tank = Deploy<MedicOperator>(400, 120);   // 30%, most HP missing
            OperatorBase squishy = Deploy<MedicOperator>(50, 10);  // 20%
            Deploy<MedicOperator>(100, 60);                        // 60%

            var all = new List<OperatorBase> { tank, squishy };
            Assert.AreSame(squishy, PanaceaOperator.ChooseHealTarget(all));
        }

        [Test]
        public void SkipsFullHpOperators_AndReturnsNullWhenNobodyIsHurt()
        {
            OperatorBase a = Deploy<MedicOperator>(100, 100);
            OperatorBase b = Deploy<MedicOperator>(80, 80);
            Assert.IsNull(PanaceaOperator.ChooseHealTarget(new List<OperatorBase> { a, b }));

            OperatorBase hurt = Deploy<MedicOperator>(100, 99);
            Assert.AreSame(hurt, PanaceaOperator.ChooseHealTarget(new List<OperatorBase> { a, b, hurt }));
        }

        [Test]
        public void TieOnHpPercent_GoesToTheLowerAbsoluteHp()
        {
            OperatorBase big = Deploy<MedicOperator>(200, 100);   // 50%
            OperatorBase small = Deploy<MedicOperator>(100, 50);  // 50%
            Assert.AreSame(small, PanaceaOperator.ChooseHealTarget(new List<OperatorBase> { big, small }));
        }

        [Test]
        public void SkipsOperatorsThatCannotBeHealed_LikeDrawgoo()
        {
            OperatorBase drawgoo = Deploy<SpecialistOperator>(150, 10);  // ~7%, but unhealable
            drawgoo.Data.cannotBeHealed = true;
            OperatorBase other = Deploy<MedicOperator>(100, 80);

            Assert.IsFalse(drawgoo.CanReceiveHealing);
            Assert.AreSame(other, PanaceaOperator.ChooseHealTarget(new List<OperatorBase> { drawgoo, other }));

            int before = drawgoo.CurrentHP;
            drawgoo.Heal(50);
            Assert.AreEqual(before, drawgoo.CurrentHP, "Direct heals do nothing either");
        }

        [Test]
        public void CanPickItself_AndIgnoresUndeployedOperators()
        {
            OperatorBase panacea = Deploy<PanaceaOperator>(85, 20);
            OperatorBase other = Deploy<MedicOperator>(100, 50);
            Assert.AreSame(panacea, PanaceaOperator.ChooseHealTarget(new List<OperatorBase> { panacea, other }));

            panacea.Retreat();
            Assert.AreSame(other, PanaceaOperator.ChooseHealTarget(new List<OperatorBase> { panacea, other }));
        }
    }
}
