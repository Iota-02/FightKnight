using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

public class NetworkPlayer : NetworkBehaviour
{
    public float moveSpeed = 5f;
    public float jumpForce = 10f;
    private Rigidbody2D rb;
    private Animator animator;
    private int facingDirection = 1; 
    private bool isGrounded = true;
    private bool isBlocking = false;
    private float timeSinceAttack = 0f;
    private int attackStage = 0;

    [SerializeField] private float maxHealth = 100f;
    public NetworkVariable<float> currentHealth = new NetworkVariable<float>(
    100f,
    NetworkVariableReadPermission.Everyone,  
    NetworkVariableWritePermission.Server    
);
    [SerializeField] private Slider healthBar;
    private bool isInvincible = false;
    [SerializeField] private float invincibilityDuration = 1.0f;

    private Vector3 hostSpawnPosition = new Vector3(-7.5f, 2.5f, 0f);  
    private Vector3 clientSpawnPosition = new Vector3(7.5f, 2.5f, 0f);

    public AudioSource attackAudioSource;
    public AudioSource walkAudioSource;
    public AudioSource blockAudioSource;
    public AudioSource jumpAudioSource;
    public AudioSource hurtAudioSource;
    public AudioSource deathAudioSource;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        AssignHealthBar();
        FindAudioSources();
    }

    void Update()
    {
        if (!IsOwner) return;

        timeSinceAttack += Time.deltaTime;
        float move = Input.GetAxis("Horizontal");

        if (move != 0)
            MoveServerRpc(move);
        else
            StopMovingServerRpc();

        if (Input.GetKeyDown(KeyCode.Space) && isGrounded) JumpServerRpc();
        if (Input.GetMouseButtonDown(0) && timeSinceAttack > 0.25f && !isBlocking) AttackServerRpc();

        if (Input.GetMouseButtonDown(1)) BlockServerRpc();
        if (Input.GetMouseButtonUp(1)) UnblockServerRpc();

        UpdateGroundedCheck();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsOwner)
        {
            Vector3 spawnPosition = IsHost ? hostSpawnPosition : clientSpawnPosition;
            transform.position = spawnPosition;
            SetPositionServerRpc(spawnPosition);

            int direction = IsHost ? 1 : -1;
            FlipCharacterServerRpc(direction);
        }
    }

    void AssignHealthBar()
    {
        if (IsOwner)
        {
            if (IsHost)
                healthBar = GameObject.FindGameObjectWithTag("HostHealthBar").GetComponent<Slider>();
            else
                healthBar = GameObject.FindGameObjectWithTag("ClientHealthBar").GetComponent<Slider>();
        }
    }

    void FindAudioSources()
    {
        attackAudioSource = FindAudioSourceByTag("AttackSound");
        walkAudioSource = FindAudioSourceByTag("WalkSound");
        blockAudioSource = FindAudioSourceByTag("BlockSound");
        jumpAudioSource = FindAudioSourceByTag("JumpSound");
        hurtAudioSource = FindAudioSourceByTag("HurtSound");
        deathAudioSource = FindAudioSourceByTag("DeathSound");
    }

    AudioSource FindAudioSourceByTag(string tag)
    {
        GameObject soundObject = GameObject.FindGameObjectWithTag(tag);
        if (soundObject != null)
        {
            return soundObject.GetComponent<AudioSource>();
        }
        else
        {
            return null;
        }
    }

    [ServerRpc]
    void FlipCharacterServerRpc(int direction, ServerRpcParams rpcParams = default)
    {
        FlipCharacterClientRpc(direction);
    }

    [ServerRpc]
    void SetPositionServerRpc(Vector3 position, ServerRpcParams rpcParams = default)
    {
        transform.position = position;
        SetPositionClientRpc(position);
    }

    [ClientRpc]
    void SetPositionClientRpc(Vector3 position)
    {
        if (!IsOwner) transform.position = position;
    }

    private void UpdateGroundedCheck()
    {
        if (rb.velocity.y < 0.1f && rb.velocity.y > -0.1f && isGrounded == false)
        {
            UpdateGroundedStateClientRpc(true);
        }
    }

    [ServerRpc]
    void MoveServerRpc(float move)
    {
        if (isBlocking) return;

        rb.velocity = new Vector2(move * moveSpeed, rb.velocity.y);

        if (move > 0 && facingDirection == -1)
        {
            facingDirection = 1;
            FlipCharacterClientRpc(facingDirection);
        }
        else if (move < 0 && facingDirection == 1)
        {
            facingDirection = -1;
            FlipCharacterClientRpc(facingDirection);
        }

        UpdateMovementStateClientRpc(1);
        UpdatePositionClientRpc(transform.position);

        if (isGrounded && move != 0 && !walkAudioSource.isPlaying)
        {
            PlayWalkSoundClientRpc();
        }
        else if (move == 0 || !isGrounded)
        {
            StopWalkSoundClientRpc();
        }
    }

    [ServerRpc]
    void StopMovingServerRpc()
    {
        if (isGrounded)
        {
            UpdateMovementStateClientRpc(0);
        }

        StopWalkSoundClientRpc();
    }

    [ServerRpc]
    void JumpServerRpc()
    {
        if (isGrounded && !isBlocking)
        {
            rb.velocity = new Vector2(rb.velocity.x, jumpForce);
            isGrounded = false;
            JumpClientRpc();
            UpdateGroundedStateClientRpc(false);
            PlayJumpSoundClientRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(float damage, ulong targetClientId)
    {
        if (!IsServer) return;

        foreach (var player in FindObjectsOfType<NetworkPlayer>())
        {
            if (player.OwnerClientId == targetClientId)
            {
                if (player.isBlocking || player.isInvincible || player.currentHealth.Value <= 0)
                {
                    return;
                }

                player.currentHealth.Value -= damage;
                player.currentHealth.Value = Mathf.Clamp(player.currentHealth.Value, 0f, player.maxHealth);

                player.UpdateHealthClientRpc(player.currentHealth.Value, targetClientId);
                player.HurtClientRpc();

                if (player.currentHealth.Value <= 0)
                {
                    player.DieClientRpc();
                }
                else
                {
                    player.StartInvincibilityServerRpc();
                }
                return;
            }
        }
    }

    [ServerRpc]
    void StartInvincibilityServerRpc()
    {
        isInvincible = true;
        Invoke(nameof(EndInvincibilityServerRpc), invincibilityDuration);
    }

    [ServerRpc]
    void EndInvincibilityServerRpc()
    {
        isInvincible = false;
    }

    [ClientRpc]
    void HurtClientRpc()
    {
        animator.SetTrigger("Hurt");
    }

    [ClientRpc]
    void DieClientRpc()
    {
        animator.SetTrigger("Death");
        rb.velocity = Vector2.zero;
        this.enabled = false;
        PlayDeathSoundClientRpc();
    }

    [ClientRpc]
    void UpdateHealthClientRpc(float newHealth, ulong targetClientId)
    {
        if (OwnerClientId == targetClientId) 
        {
            healthBar.value = newHealth / maxHealth; 
        }
    }

    [ClientRpc]
    void JumpClientRpc()
    {
        animator.SetTrigger("Jump");
    }

    [ServerRpc]
    void AttackServerRpc()
    {
        attackStage++;
        if (attackStage > 3) attackStage = 1;
        if (timeSinceAttack > 1.0f) attackStage = 1;

        AttackClientRpc(attackStage);
        PlayAttackSoundClientRpc();

        Vector2 attackPosition = transform.position + new Vector3(facingDirection * 1.5f, 0, 0);
        Collider2D[] hitObjects = Physics2D.OverlapBoxAll(attackPosition, new Vector2(2f, 1f), 0);

        foreach (Collider2D obj in hitObjects)
        {
            if (obj.CompareTag("Player") && obj.gameObject != gameObject)
            {
                float damage = attackStage == 3 ? 20f : (attackStage == 2 ? 15f : 10f);
                NetworkObject enemyNetworkObject = obj.GetComponent<NetworkObject>();

                if (enemyNetworkObject != null && enemyNetworkObject.IsSpawned)
                {
                    obj.GetComponent<NetworkPlayer>().TakeDamageServerRpc(damage, enemyNetworkObject.OwnerClientId);
                }
            }
        }

        timeSinceAttack = 0.0f;
    }

    [ServerRpc]
    void BlockServerRpc()
    {
        isBlocking = true;
        BlockClientRpc();
        PlayBlockSoundClientRpc();
    }

    [ServerRpc]
    void UnblockServerRpc()
    {
        isBlocking = false;
        UnblockClientRpc();
        StopBlockSoundClientRpc();
    }

    [ClientRpc]
    void AttackClientRpc(int stage)
    {
        animator.SetTrigger("Attack" + stage);
    }

    [ClientRpc]
    void BlockClientRpc()
    {
        animator.SetTrigger("Block");  
        animator.SetBool("IdleBlock", true);  
    }

    [ClientRpc]
    void UnblockClientRpc()
    {
        animator.SetBool("IdleBlock", false);  
    }

    [ClientRpc]
    void UpdatePositionClientRpc(Vector3 newPosition)
    {
        if (!IsOwner) transform.position = newPosition;
    }

    [ClientRpc]
    void UpdateGroundedStateClientRpc(bool grounded)
    {
        isGrounded = grounded;
        animator.SetBool("Grounded", grounded);
    }

    [ClientRpc]
    void UpdateMovementStateClientRpc(int animState)
    {
        animator.SetInteger("AnimState", animState);
    }

    [ClientRpc]
    void FlipCharacterClientRpc(int direction)
    {
        transform.localScale = new Vector3(direction, 1, 1);
    }

    [ClientRpc]
    void PlayAttackSoundClientRpc()
    {
        if (attackAudioSource != null) attackAudioSource.Play();
    }

    [ClientRpc]
    void PlayWalkSoundClientRpc()
    {
        if (walkAudioSource != null) walkAudioSource.Play();
    }

    [ClientRpc]
    void StopWalkSoundClientRpc()
    {
        if (walkAudioSource != null) walkAudioSource.Stop();
    }

    [ClientRpc]
    void PlayBlockSoundClientRpc()
    {
        if (blockAudioSource != null) blockAudioSource.Play();
    }

    [ClientRpc]
    void StopBlockSoundClientRpc()
    {
        if (blockAudioSource != null) blockAudioSource.Stop();
    }

    [ClientRpc]
    void PlayJumpSoundClientRpc()
    {
        if (jumpAudioSource != null) jumpAudioSource.Play();
    }

    [ClientRpc]
    void PlayHurtSoundClientRpc()
    {
        if (hurtAudioSource != null) hurtAudioSource.Play();
    }

    [ClientRpc]
    void PlayDeathSoundClientRpc()
    {
        if (deathAudioSource != null) deathAudioSource.Play();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
        {
            UpdateGroundedStateClientRpc(true);
        }
    }
}