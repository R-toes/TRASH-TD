using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Shadeslice — 3-Star Guard.
    /// Ability: Deals bonus damage while isolated (no allied operator in the diamond pattern).
    /// When not attacked for 5 seconds restore 2.5% of max hp every second.
    /// </summary>
    public class ShadesliceOperator : GuardOperator
    {
        public const float OutOfCombatDuration = 5.0f;
        public const float RegenIntervalSeconds = 1.0f;
        public const float RegenPercentOfMaxHP = 0.025f;
        public const float ParryDamageReduction = 0.35f;
        public const float ParryReflectionPercent = 0.20f;
        public const float ParryReflectionCooldown = 0.6f;
        public const int AttacksPerPhantomPursuit = 3;
        public const float PhantomAttackMultiplier = 3.0f;
        private const float AttackEffectYOffset = -2f / 32f;
        private const float PhantomDissolveDuration = 0.1f;
        private const float PhantomMaterializeDuration = 0.12f;
        private const float PhantomFrameDuration = 0.035f;
        private const float PhantomRecoverDuration = 0.08f;
        private static readonly Color ParryColor = new Color(0.55f, 0.78f, 1f, 1f);
        private static readonly Color PhantomColor = new Color(0.68f, 0.35f, 1f, 1f);
        private static readonly Color PhantomShadowColor = new Color(0.12f, 0.08f, 0.18f, 1f);
        private static readonly Color PhantomVioletColor = new Color(0.48f, 0.22f, 0.78f, 1f);

        private float timeSinceLastAttacked;
        private float regenTimer = 1.0f;
        private ShadesliceSpriteAnimation spriteAnimation;
        private readonly Dictionary<EnemyBase, float> nextReflectionTimeByAttacker = new Dictionary<EnemyBase, float>();
        private readonly List<EnemyBase> expiredReflectionAttackers = new List<EnemyBase>();
        private SpriteRenderer parryOverlay;
        private Coroutine parryEffectCoroutine;
        private Coroutine phantomPursuitCoroutine;
        private GameObject phantomVisual;
        private SpriteRenderer[] hiddenSpriteRenderers;
        private bool[] hiddenSpriteRendererStates;
        private Color[] hiddenSpriteRendererColors;
        private bool processingDirectAttack;
        private int successfulNormalAttackCount;
        private int queuedPhantomPursuits;
        private float nextParryVisualTime;

        public float TimeSinceLastAttacked => timeSinceLastAttacked;
        public bool IsRegenerating => isDeployed && !IsCombatPaused && timeSinceLastAttacked >= OutOfCombatDuration && currentHP < maxHP;
        public bool HasFirstUpgrade => currentRarity >= OperatorRarity.Star4;
        public bool HasSecondUpgrade => currentRarity >= OperatorRarity.Star5;
        public int SuccessfulNormalAttackCount => successfulNormalAttackCount;

        private ShadesliceSpriteAnimation SpriteAnimation
        {
            get
            {
                if (spriteAnimation == null) spriteAnimation = GetComponent<ShadesliceSpriteAnimation>();
                return spriteAnimation;
            }
        }

        public override void Initialize(OperatorData operatorData, OperatorRarity rarity, OperatorFacing operatorFacing = OperatorFacing.Right)
        {
            CancelUpgradeVisuals();
            nextReflectionTimeByAttacker.Clear();
            successfulNormalAttackCount = 0;
            queuedPhantomPursuits = 0;
            processingDirectAttack = false;
            base.Initialize(operatorData, rarity, operatorFacing);
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void OnDisable()
        {
            CancelUpgradeVisuals();
            queuedPhantomPursuits = 0;
        }

        private void UpdateAttackWindup()
        {
            ShadesliceSpriteAnimation anim = SpriteAnimation;
            if (anim == null || !anim.HasAttackAnimation) return;

            if (!isDeployed)
            {
                anim.CancelAttack();
                return;
            }

            if (IsCombatPaused || isStunned || isBound)
            {
                anim.CancelWindup();
                return;
            }

            if (anim.IsWindingUp)
            {
                EnemyBase windupTarget = FindTarget();
                if (windupTarget == null || !CanAttackTarget(windupTarget))
                    anim.CancelWindup();
                return;
            }

            if (anim.IsRecovering) return;

            EnemyBase target = FindTarget();
            if (target == null || !CanAttackTarget(target)) return;

            float attackInterval = GetAttackInterval();
            float timeUntilImpact = attackInterval - attackTimer;
            if (timeUntilImpact > ShadesliceSpriteAnimation.WindupSeconds) return;

            if (timeUntilImpact <= 0f)
            {
                timeUntilImpact = ShadesliceSpriteAnimation.WindupSeconds;
                attackTimer = Mathf.Max(0f, attackInterval - timeUntilImpact);
            }

            anim.BeginAttack(timeUntilImpact);
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed || isStunned || isBound) return;

            SpriteAnimation?.BeginImpact();
            HasFoughtInWave = true;
            DeployedDuringCurrentPreparation = false;
            PlayAttackSound();

            bool isIsolated = data != null && data.bonusWhenIsolated && !HasAdjacentOperator();
            int attackPower = isIsolated
                ? Mathf.RoundToInt(currentATK * Mathf.Max(1f, data.isolatedAttackMultiplier))
                : CurrentATK;
            Vector3 projectileStart = transform.position + Vector3.up * AttackEffectYOffset;
            Vector3 projectileEnd = GetFixedAttackEffectEnd(projectileStart);

            if (isIsolated)
            {
                MeleeSwipeVisual.PlayStab(
                    projectileStart,
                    projectileEnd,
                    new Color(0.12f, 0.008f, 0.015f, 1f));
            }
            else
            {
                MeleeSwipeVisual.Play(
                    projectileStart,
                    projectileEnd,
                    new Color(1f, 0.78f, 0.3f, 1f),
                    MeleeSlashSize,
                    MeleeSlashStyle);
            }

            CombatProjectileVisual.Fire(
                projectileStart,
                projectileEnd,
                isIsolated ? new Color(1f, 0.2f, 0.15f) : new Color(0.7f, 0.35f, 0.9f),
                12f,
                0.1f,
                0.05f,
                false,
                () => ApplyAttackDamage(target, attackPower));
        }

        private Vector3 GetFixedAttackEffectEnd(Vector3 start)
        {
            Vector3 direction = facing == OperatorFacing.Right ? Vector3.right : Vector3.left;
            GridManager gridManager = FindAnyObjectByType<GridManager>();
            float range = gridManager != null ? gridManager.CellSize : 1f;
            return start + direction * range;
        }

        private void ApplyAttackDamage(EnemyBase target, int attackPower)
        {
            if (target == null || target.IsDead) return;

            int damage = DamageCalculator.CalculateDamage(attackPower, GetTargetMitigation(target));
            if (target.TryTakeAttackDamage(damage, data.damageType, this))
            {
                PlayImpactSound();
                RegisterSuccessfulNormalAttack();
            }
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            SpriteAnimation?.CancelAttack();
            nextReflectionTimeByAttacker.Clear();
            successfulNormalAttackCount = 0;
            queuedPhantomPursuits = 0;
            ResetNotAttackedTimer();
        }

        protected override void OnRetreated()
        {
            CancelUpgradeVisuals();
            nextReflectionTimeByAttacker.Clear();
            successfulNormalAttackCount = 0;
            queuedPhantomPursuits = 0;
            SpriteAnimation?.CancelAttack();
            base.OnRetreated();
            ResetNotAttackedTimer();
        }

        public void ResetNotAttackedTimer()
        {
            timeSinceLastAttacked = 0f;
            regenTimer = RegenIntervalSeconds;
        }

        public void SetTimeSinceLastAttackedForTesting(float time)
        {
            timeSinceLastAttacked = time;
        }

        public override void TakeDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            base.TakeDamage(rawATK, damageType, attacker);
            ResetNotAttackedTimer();
        }

        protected override int ModifyDamageAfterMitigation(int damage, DamageType damageType, EnemyBase attacker)
        {
            return HasFirstUpgrade && processingDirectAttack && attacker != null
                ? Mathf.RoundToInt(damage * (1f - ParryDamageReduction))
                : damage;
        }

        protected override void OnDamageApplied(int calculatedDamage, int actualDamage, DamageType damageType, EnemyBase attacker)
        {
            if (!HasFirstUpgrade || !processingDirectAttack || attacker == null) return;

            processingDirectAttack = false;
            try
            {
                TryReflectDamage(attacker, calculatedDamage);
                PlayParryVisual();
            }
            finally
            {
                processingDirectAttack = true;
            }
        }

        public override bool TryTakeAttackDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            if (!CanBeTargetedByEnemy(attacker)) return false;

            bool wasDirectAttack = processingDirectAttack;
            processingDirectAttack = attacker != null;
            try
            {
                return base.TryTakeAttackDamage(rawATK, damageType, attacker);
            }
            finally
            {
                processingDirectAttack = wasDirectAttack;
            }
        }

        private void TryReflectDamage(EnemyBase attacker, int originalDamage)
        {
            if (attacker == null || attacker.IsDead || originalDamage <= 0) return;

            float now = Time.time;
            PruneReflectionCooldowns(now);
            if (nextReflectionTimeByAttacker.TryGetValue(attacker, out float nextAllowed) && now < nextAllowed)
                return;

            nextReflectionTimeByAttacker[attacker] = now + ParryReflectionCooldown;
            int reflectedDamage = Mathf.RoundToInt(originalDamage * ParryReflectionPercent);
            if (reflectedDamage > 0)
                attacker.TakeDamage(reflectedDamage, DamageType.True);
        }

        private void PruneReflectionCooldowns(float now)
        {
            expiredReflectionAttackers.Clear();
            foreach (KeyValuePair<EnemyBase, float> cooldown in nextReflectionTimeByAttacker)
            {
                if (cooldown.Key == null || cooldown.Value <= now)
                    expiredReflectionAttackers.Add(cooldown.Key);
            }

            for (int i = 0; i < expiredReflectionAttackers.Count; i++)
                nextReflectionTimeByAttacker.Remove(expiredReflectionAttackers[i]);
        }

        private void PlayParryVisual()
        {
            if (!Application.isPlaying) return;

            if (Time.unscaledTime >= nextParryVisualTime)
            {
                nextParryVisualTime = Time.unscaledTime + 0.18f;
                FloatingCombatNumber.ShowText(transform.position, "PARRY", ParryColor);
                MeleeSwipeVisual.Play(transform.position, transform.position + Vector3.up * 0.2f,
                    new Color(0.55f, 0.78f, 1f, 0.8f), 0.75f, MeleeStyle.Impact);
            }

            if (parryOverlay == null)
                parryOverlay = CreateParryOverlay();
            if (parryOverlay == null || parryEffectCoroutine != null) return;

            parryEffectCoroutine = StartCoroutine(AnimateParryOverlay());
        }

        private SpriteRenderer CreateParryOverlay()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null) return null;

            GameObject overlayObject = new GameObject("ShadesliceParryOverlay");
            overlayObject.transform.SetParent(transform, false);
            SpriteRenderer overlay = overlayObject.AddComponent<SpriteRenderer>();
            overlay.sprite = spriteRenderer.sprite;
            overlay.flipX = spriteRenderer.flipX;
            overlay.flipY = spriteRenderer.flipY;
            overlay.sortingLayerID = spriteRenderer.sortingLayerID;
            overlay.sortingOrder = spriteRenderer.sortingOrder + 1;
            overlay.sharedMaterial = spriteRenderer.sharedMaterial;
            overlay.color = Color.clear;
            return overlay;
        }

        private IEnumerator AnimateParryOverlay()
        {
            const float duration = 0.22f;
            float elapsed = 0f;
            while (elapsed < duration && parryOverlay != null && spriteRenderer != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                parryOverlay.sprite = spriteRenderer.sprite;
                parryOverlay.flipX = spriteRenderer.flipX;
                parryOverlay.flipY = spriteRenderer.flipY;
                parryOverlay.transform.localScale = Vector3.one * Mathf.Lerp(1.08f, 1.2f, progress);
                parryOverlay.color = new Color(ParryColor.r, ParryColor.g, ParryColor.b, 0.32f * (1f - progress));
                yield return null;
            }

            if (parryOverlay != null)
            {
                parryOverlay.color = Color.clear;
                parryOverlay.transform.localScale = Vector3.one;
            }
            parryEffectCoroutine = null;
        }

        private void RegisterSuccessfulNormalAttack()
        {
            if (!HasSecondUpgrade) return;

            successfulNormalAttackCount++;
            if (successfulNormalAttackCount < AttacksPerPhantomPursuit) return;

            successfulNormalAttackCount = 0;
            queuedPhantomPursuits++;
            if (Application.isPlaying && phantomPursuitCoroutine == null)
                phantomPursuitCoroutine = StartCoroutine(ProcessPhantomPursuits());
        }

        private IEnumerator ProcessPhantomPursuits()
        {
            yield return null;
            while (queuedPhantomPursuits > 0 && isDeployed && currentHP > 0)
            {
                queuedPhantomPursuits--;
                yield return StartCoroutine(PerformPhantomPursuit());
            }

            phantomPursuitCoroutine = null;
        }

        private IEnumerator PerformPhantomPursuit()
        {
            try
            {
                EnemyBase target = FindClosestToExitTarget(EnemyManager.Instance != null
                    ? EnemyManager.Instance.ActiveEnemies
                    : null);
                if (target == null || !isDeployed || currentHP <= 0) yield break;

                Vector3 originalPosition = spriteRenderer != null ? spriteRenderer.bounds.center : transform.position;
                CacheOriginalSprites();
                PlayShadowDeparture(originalPosition);
                float dissolveElapsed = 0f;
                while (dissolveElapsed < PhantomDissolveDuration)
                {
                    if (IsCombatPaused || !isDeployed || currentHP <= 0) yield break;
                    dissolveElapsed += Time.unscaledDeltaTime;
                    ApplyOriginalSpriteDissolve(Mathf.SmoothStep(
                        0f, 1f, Mathf.Clamp01(dissolveElapsed / PhantomDissolveDuration)));
                    yield return null;
                }

                HideOriginalSprites();
                Sprite[] attackFrames = SpriteAnimation != null ? SpriteAnimation.AttackFrames : null;
                bool struck = false;
                while (!struck && isDeployed && currentHP > 0 && !IsCombatPaused)
                {
                    if (!IsValidPhantomTarget(target))
                        target = FindClosestToExitTarget(EnemyManager.Instance != null
                            ? EnemyManager.Instance.ActiveEnemies
                            : null);
                    if (target == null)
                    {
                        yield return StartCoroutine(MaterializeOriginalSprites(originalPosition));
                        yield break;
                    }

                    Vector3 targetPosition = GetPhantomPosition(target);
                    PlayShadowArrival(targetPosition);
                    SpriteRenderer phantom = CreatePhantomVisual(target, targetPosition);
                    if (phantom == null)
                    {
                        yield return StartCoroutine(MaterializeOriginalSprites(originalPosition));
                        yield break;
                    }

                    float materializeElapsed = 0f;
                    while (materializeElapsed < PhantomMaterializeDuration)
                    {
                        if (IsCombatPaused || !isDeployed || currentHP <= 0) yield break;
                        if (!IsValidPhantomTarget(target)) break;
                        materializeElapsed += Time.unscaledDeltaTime;
                        float progress = Mathf.SmoothStep(
                            0f, 1f, Mathf.Clamp01(materializeElapsed / PhantomMaterializeDuration));
                        UpdatePhantomMaterialization(phantom, progress);
                        yield return null;
                    }

                    if (!IsValidPhantomTarget(target))
                    {
                        DestroyTransientVisual(phantomVisual);
                        phantomVisual = null;
                        target = FindClosestToExitTarget(EnemyManager.Instance != null
                            ? EnemyManager.Instance.ActiveEnemies
                            : null);
                        continue;
                    }

                    phantom.transform.position = GetPhantomPosition(target);
                    phantom.color = new Color(PhantomColor.r, PhantomColor.g, PhantomColor.b, 0.82f);
                    for (int i = 0; i < 4; i++)
                    {
                        if (IsCombatPaused || !isDeployed || currentHP <= 0) yield break;
                        if (!IsValidPhantomTarget(target)) break;
                        if (attackFrames != null && attackFrames.Length > 0)
                            phantom.sprite = attackFrames[Mathf.Min(i * 2, attackFrames.Length - 1)];
                        phantom.transform.position = GetPhantomPosition(target);
                        yield return new WaitForSecondsRealtime(PhantomFrameDuration);
                    }

                    if (!IsValidPhantomTarget(target))
                    {
                        DestroyTransientVisual(phantomVisual);
                        phantomVisual = null;
                        target = FindClosestToExitTarget(EnemyManager.Instance != null
                            ? EnemyManager.Instance.ActiveEnemies
                            : null);
                        continue;
                    }

                    phantom.sprite = attackFrames != null && attackFrames.Length > ShadesliceSpriteAnimation.ImpactFrameIndex
                        ? attackFrames[ShadesliceSpriteAnimation.ImpactFrameIndex]
                        : (spriteRenderer != null ? spriteRenderer.sprite : phantom.sprite);
                    Vector3 impactPosition = target.VisualPosition;
                    PlayShadowStrike(impactPosition);
                    MeleeSwipeVisual.PlayStab(phantom.transform.position, impactPosition, PhantomColor);
                    ApplyPhantomDamage(target);
                    yield return new WaitForSecondsRealtime(PhantomRecoverDuration);
                    PlayShadowDeparture(phantom.transform.position);
                    yield return StartCoroutine(FadePhantomOut(phantom));
                    DestroyTransientVisual(phantomVisual);
                    phantomVisual = null;
                    PlayShadowArrival(originalPosition);
                    yield return StartCoroutine(MaterializeOriginalSprites(originalPosition));
                    struck = true;
                }
            }
            finally
            {
                DestroyTransientVisual(phantomVisual);
                phantomVisual = null;
                RestoreOriginalSprites();
            }
        }

        private void ApplyPhantomDamage(EnemyBase target)
        {
            if (!IsValidPhantomTarget(target) || data == null) return;

            bool isIsolated = data.bonusWhenIsolated && !HasAdjacentOperator();
            int attackPower = CalculatePhantomAttackPower(
                CurrentATK,
                data.isolatedAttackMultiplier,
                isIsolated);
            int damage = DamageCalculator.CalculateDamage(attackPower, GetTargetMitigation(target));
            if (target.TryTakeAttackDamage(damage, data.damageType, this))
                PlayImpactSound();
        }

        public static int CalculatePhantomAttackPower(int currentAttack, float isolatedMultiplier, bool isIsolated)
        {
            float isolation = isIsolated ? Mathf.Max(1f, isolatedMultiplier) : 1f;
            return Mathf.Max(0, Mathf.RoundToInt(currentAttack * PhantomAttackMultiplier * isolation));
        }

        public static EnemyBase FindClosestToExitTarget(IReadOnlyList<EnemyBase> candidates)
        {
            if (candidates == null) return null;

            EnemyBase closest = null;
            float shortestRemainingDistance = float.PositiveInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                EnemyBase candidate = candidates[i];
                if (!IsValidPhantomTarget(candidate) || candidate.Path == null || candidate.Path.Count == 0)
                    continue;

                int pathIndex = Mathf.Clamp(candidate.CurrentPathIndex, 0, candidate.Path.Count - 1);
                float remainingDistance = Vector3.Distance(candidate.transform.position, candidate.Path[pathIndex]);
                for (int pathPoint = pathIndex + 1; pathPoint < candidate.Path.Count; pathPoint++)
                    remainingDistance += Vector3.Distance(candidate.Path[pathPoint - 1], candidate.Path[pathPoint]);

                if (remainingDistance < shortestRemainingDistance)
                {
                    shortestRemainingDistance = remainingDistance;
                    closest = candidate;
                }
            }

            return closest;
        }

        private static bool IsValidPhantomTarget(EnemyBase target) =>
            target != null && !target.IsDead && !target.IsInsideHole && target.CurrentHP > 0 && target.gameObject.activeInHierarchy;

        private void CacheOriginalSprites()
        {
            hiddenSpriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            hiddenSpriteRendererStates = new bool[hiddenSpriteRenderers.Length];
            hiddenSpriteRendererColors = new Color[hiddenSpriteRenderers.Length];
            for (int i = 0; i < hiddenSpriteRenderers.Length; i++)
            {
                hiddenSpriteRendererStates[i] = hiddenSpriteRenderers[i] != null && hiddenSpriteRenderers[i].enabled;
                hiddenSpriteRendererColors[i] = hiddenSpriteRenderers[i] != null
                    ? hiddenSpriteRenderers[i].color
                    : Color.white;
            }
        }

        private void ApplyOriginalSpriteDissolve(float progress)
        {
            if (hiddenSpriteRenderers == null || hiddenSpriteRendererColors == null) return;
            Color shadowColor = PhantomShadowColor;
            for (int i = 0; i < hiddenSpriteRenderers.Length; i++)
            {
                SpriteRenderer renderer = hiddenSpriteRenderers[i];
                if (renderer == null) continue;

                Color original = hiddenSpriteRendererColors[i];
                Color color = Color.Lerp(original, shadowColor, progress);
                color.a = original.a * (1f - progress);
                renderer.color = color;
            }
        }

        private void HideOriginalSprites()
        {
            if (hiddenSpriteRenderers == null) return;
            for (int i = 0; i < hiddenSpriteRenderers.Length; i++)
                if (hiddenSpriteRenderers[i] != null)
                    hiddenSpriteRenderers[i].enabled = false;
        }

        private IEnumerator MaterializeOriginalSprites(Vector3 position)
        {
            if (hiddenSpriteRenderers == null || hiddenSpriteRendererColors == null) yield break;

            for (int i = 0; i < hiddenSpriteRenderers.Length; i++)
            {
                SpriteRenderer renderer = hiddenSpriteRenderers[i];
                if (renderer == null) continue;
                renderer.enabled = hiddenSpriteRendererStates[i];
                Color color = PhantomShadowColor;
                color.a = 0f;
                renderer.color = color;
            }

            float elapsed = 0f;
            while (elapsed < PhantomMaterializeDuration)
            {
                if (!isDeployed || currentHP <= 0 || IsCombatPaused) yield break;
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(
                    0f, 1f, Mathf.Clamp01(elapsed / PhantomMaterializeDuration));
                for (int i = 0; i < hiddenSpriteRenderers.Length; i++)
                {
                    SpriteRenderer renderer = hiddenSpriteRenderers[i];
                    if (renderer == null) continue;
                    Color color = Color.Lerp(PhantomShadowColor, hiddenSpriteRendererColors[i], progress);
                    color.a = hiddenSpriteRendererColors[i].a * progress;
                    renderer.color = color;
                }
                yield return null;
            }

            RestoreOriginalSprites();
        }

        private void RestoreOriginalSprites()
        {
            if (hiddenSpriteRenderers == null || hiddenSpriteRendererStates == null) return;
            for (int i = 0; i < hiddenSpriteRenderers.Length; i++)
            {
                if (hiddenSpriteRenderers[i] != null)
                {
                    hiddenSpriteRenderers[i].enabled = hiddenSpriteRendererStates[i];
                    if (hiddenSpriteRendererColors != null && i < hiddenSpriteRendererColors.Length)
                        hiddenSpriteRenderers[i].color = hiddenSpriteRendererColors[i];
                }
            }

            hiddenSpriteRenderers = null;
            hiddenSpriteRendererStates = null;
            hiddenSpriteRendererColors = null;
        }

        private SpriteRenderer CreatePhantomVisual(EnemyBase target, Vector3 position)
        {
            if (target == null || spriteRenderer == null || spriteRenderer.sprite == null) return null;

            phantomVisual = new GameObject("ShadeslicePhantom");
            phantomVisual.transform.position = position;
            phantomVisual.transform.localScale = spriteRenderer.transform.lossyScale * 0.82f;
            SpriteRenderer renderer = phantomVisual.AddComponent<SpriteRenderer>();
            renderer.sprite = spriteRenderer.sprite;
            renderer.flipX = transform.position.x <= target.VisualPosition.x;
            renderer.flipY = spriteRenderer.flipY;
            renderer.transform.rotation = spriteRenderer.transform.rotation;
            renderer.sortingLayerID = spriteRenderer.sortingLayerID;
            renderer.sortingOrder = spriteRenderer.sortingOrder + 2;
            renderer.sharedMaterial = spriteRenderer.sharedMaterial;
            renderer.color = new Color(PhantomColor.r, PhantomColor.g, PhantomColor.b, 0f);
            return renderer;
        }

        private Vector3 GetPhantomPosition(EnemyBase target)
        {
            if (target == null) return Vector3.zero;
            float direction = transform.position.x <= target.VisualPosition.x ? 1f : -1f;
            return target.VisualPosition - Vector3.right * direction * 0.45f;
        }

        private void UpdatePhantomMaterialization(SpriteRenderer phantom, float progress)
        {
            if (phantom == null) return;
            phantom.color = new Color(PhantomColor.r, PhantomColor.g, PhantomColor.b, 0.82f * progress);
            phantom.transform.localScale = spriteRenderer.transform.lossyScale * Mathf.Lerp(0.82f, 1f, progress);
        }

        private IEnumerator FadePhantomOut(SpriteRenderer phantom)
        {
            if (phantom == null) yield break;
            Color initialColor = phantom.color;
            Vector3 initialScale = phantom.transform.localScale;
            float elapsed = 0f;
            while (elapsed < PhantomDissolveDuration)
            {
                if (IsCombatPaused || !isDeployed || currentHP <= 0) yield break;
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(
                    0f, 1f, Mathf.Clamp01(elapsed / PhantomDissolveDuration));
                Color color = initialColor;
                color.a = Mathf.Lerp(initialColor.a, 0f, progress);
                phantom.color = color;
                phantom.transform.localScale = initialScale * Mathf.Lerp(1f, 0.82f, progress);
                yield return null;
            }
        }

        private static void PlayShadowDeparture(Vector3 position)
        {
            CombatFx.Smoke(position, new Color(0.08f, 0.045f, 0.12f, 0.82f), 5, 0.12f, 0.38f, 0.02f);
            CombatFx.Sparks(position, PhantomVioletColor, 4, 0.45f, 0.22f, big: true);
        }

        private static void PlayShadowArrival(Vector3 position)
        {
            CombatFx.Smoke(position, new Color(0.08f, 0.045f, 0.12f, 0.78f), 6, 0.16f, 0.4f, 0f);
            CombatFx.Sparks(position, PhantomVioletColor, 5, 0.55f, 0.2f, big: true);
        }

        private static void PlayShadowStrike(Vector3 position)
        {
            CombatFx.Flash(position, PhantomVioletColor, false);
            CombatFx.Sparks(position, new Color(0.16f, 0.08f, 0.22f, 1f), 5, 0.65f, 0.2f, big: true);
        }

        private void CancelUpgradeVisuals()
        {
            if (phantomPursuitCoroutine != null) StopCoroutine(phantomPursuitCoroutine);
            if (parryEffectCoroutine != null) StopCoroutine(parryEffectCoroutine);
            phantomPursuitCoroutine = null;
            parryEffectCoroutine = null;
            DestroyTransientVisual(phantomVisual);
            phantomVisual = null;
            RestoreOriginalSprites();
            if (parryOverlay != null)
            {
                parryOverlay.color = Color.clear;
                parryOverlay.transform.localScale = Vector3.one;
            }
        }

        private static void DestroyTransientVisual(GameObject visual)
        {
            if (visual == null) return;
            if (Application.isPlaying) Destroy(visual);
            else DestroyImmediate(visual);
        }

        public override bool CanBeTargetedByEnemy(EnemyBase attacker)
        {
            return IsTargetable && CanBeAttackedBy(attacker);
        }

        public override bool TakeTrueDamage(int damage, bool ignoreShieldNegation = false, bool canDodge = false)
        {
            bool dealt = base.TakeTrueDamage(damage, ignoreShieldNegation, canDodge);
            ResetNotAttackedTimer();
            return dealt;
        }

        private bool CanBeAttackedBy(EnemyBase attacker)
        {
            if (attacker == null) return true;
            if (attacker.Data != null && attacker.Data.archetype == EnemyArchetype.Tank) return true;

            return blockedEnemies.Contains(attacker) &&
                   attacker.IsBlocked &&
                   attacker.BlockingOperator == this;
        }

        protected override void Update()
        {
            UpdateAttackWindup();
            base.Update();

            if (!isDeployed || IsCombatPaused || currentHP <= 0) return;

            UpdateOutOfCombatRegen(Time.deltaTime);
        }

        public void UpdateOutOfCombatRegen(float dt)
        {
            bool wasOutOfCombat = timeSinceLastAttacked >= OutOfCombatDuration;
            timeSinceLastAttacked += dt;
            if (!wasOutOfCombat && timeSinceLastAttacked >= OutOfCombatDuration && currentHP < maxHP)
                PlayAbilitySound("Regen");
            if (timeSinceLastAttacked >= OutOfCombatDuration)
            {
                regenTimer += dt;
                while (regenTimer >= RegenIntervalSeconds)
                {
                    regenTimer -= RegenIntervalSeconds;
                    if (currentHP > 0 && currentHP < maxHP)
                    {
                        int healAmount = Mathf.Max(1, Mathf.RoundToInt(maxHP * RegenPercentOfMaxHP));
                        HealSelf(healAmount);
                    }
                }
            }
            else
            {
                regenTimer = RegenIntervalSeconds;
            }
        }

        public bool IsInIsolationPattern(Vector2Int offset) => IsOperatorInIsolationRange(offset);

        public override Vector2Int[] GetTalentRangePattern() => OperatorData.ShadesliceTalentPattern;

        protected override bool IsOperatorInIsolationRange(Vector2Int offset)
        {
            int manhattanDistance = Mathf.Abs(offset.x) + Mathf.Abs(offset.y);
            return manhattanDistance <= 2 && (offset.x != 0 || offset.y != 0);
        }
    }
}
