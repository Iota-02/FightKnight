using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using System.Collections;

public class PlayerHealth : NetworkBehaviour
{
    public int maxHealth = 100;
    public NetworkVariable<int> currentHealth = new NetworkVariable<int>();
    public Slider healthBar; // Assign the Slider directly in the Inspector
    public Animator animator;
    public float invincibilityDuration = 1.0f;
    private float invincibilityTimer = 0f;
    private bool isInvincible = false;

    void Start()
    {
        currentHealth.Value = maxHealth;
        UpdateHealthBar();
    }

    void Update()
    {
        if (isInvincible)
        {
            invincibilityTimer -= Time.deltaTime;
            if (invincibilityTimer <= 0)
            {
                isInvincible = false;
            }
        }
    }

    public void TakeDamage(int damage)
    {
        if (!IsServer) return; // Only process damage on the server

        if (!isInvincible)
        {
            currentHealth.Value -= damage;
            UpdateHealthBarClientRpc(currentHealth.Value);
            animator.SetTrigger("Hurt");
            StartInvincibility();

            if (currentHealth.Value <= 0)
            {
                DieClientRpc();
            }
        }
    }

    private void StartInvincibility()
    {
        isInvincible = true;
        invincibilityTimer = invincibilityDuration;
    }

    [ClientRpc]
    private void UpdateHealthBarClientRpc(int health)
    {
        currentHealth.Value = health;
        UpdateHealthBar();
    }

    private void UpdateHealthBar()
    {
        if (healthBar != null)
        {
            healthBar.value = (float)currentHealth.Value / maxHealth;
        }
    }

    [ClientRpc]
    private void DieClientRpc()
    {
        animator.SetTrigger("Death");
        // Disable movement or any other logic
        GetComponent<NetworkPlayer>().enabled = false;
        GetComponent<Collider2D>().enabled = false;
    }
}