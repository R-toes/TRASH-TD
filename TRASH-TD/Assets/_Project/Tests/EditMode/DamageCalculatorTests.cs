using NUnit.Framework;
using TrashTD.Combat;
using TrashTD.Data;

namespace TrashTD.Tests
{
    public class DamageCalculatorTests
    {
        [Test]
        public void NormalPhysicalDamage_DealsAtkMinusDef()
        {
            // ATK: 100, DEF: 30 -> 100 - 30 = 70. 5% of 100 is 5. Max(70, 5) = 70.
            int damage = DamageCalculator.CalculateDamage(100, 30);
            Assert.AreEqual(70, damage);
        }

        [Test]
        public void HighDefense_AppliesFivePercentChipDamageFloor()
        {
            // ATK: 100, DEF: 200 -> 100 - 200 = -100. 5% of 100 = 5. Max(-100, 5) = 5.
            int damage = DamageCalculator.CalculateDamage(100, 200);
            Assert.AreEqual(5, damage);
        }

        [Test]
        public void ZeroOrNegativeAtk_ReturnsZeroDamage()
        {
            int damageZero = DamageCalculator.CalculateDamage(0, 50);
            Assert.AreEqual(0, damageZero);

            int damageNegative = DamageCalculator.CalculateDamage(-10, 50);
            Assert.AreEqual(0, damageNegative);
        }

        [Test]
        public void ArtsDamage_MitigatedByResIdentically()
        {
            // ATK: 80, RES: 20 -> 60
            int damage = DamageCalculator.CalculateDamage(80, 0, 20, DamageType.Arts);
            Assert.AreEqual(60, damage);

            // ATK: 80, RES: 100 -> min floor = 80 * 0.05 = 4
            int floorDamage = DamageCalculator.CalculateDamage(80, 0, 100, DamageType.Arts);
            Assert.AreEqual(4, floorDamage);
        }
    }
}
