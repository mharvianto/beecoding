<script setup>
import { tr } from '../lib/i18n';
import Icon from './Icon.vue';
import RowMenu from './RowMenu.vue';

// The per-user actions in Admin > Users, folded into one "⋮" menu. The parent owns what each action does.
defineProps({ user: { type: Object, required: true } });
defineEmits(['verify', 'reset-link', 'reset-mfa', 'grant-admin', 'revoke-admin', 'delete']);
</script>

<template>
  <RowMenu :label="`Actions for ${user.email}`">
    <button v-if="!user.emailVerified" role="menuitem" class="row-menu-item" @click="$emit('verify')">{{ tr('✓ Mark email verified') }}</button>
    <button role="menuitem" class="row-menu-item" @click="$emit('reset-link')"><Icon name="key" /> {{ tr('Password reset link') }}</button>
    <button v-if="user.mfaEnabled" role="menuitem" class="row-menu-item" @click="$emit('reset-mfa')"><Icon name="unlock" /> {{ tr('Reset two-step verification') }}</button>
    <div class="row-menu-sep" role="separator"></div>
    <button v-if="!user.isAdmin" role="menuitem" class="row-menu-item" @click="$emit('grant-admin')"><Icon name="shield" /> {{ tr('Make admin') }}</button>
    <button v-else role="menuitem" class="row-menu-item" @click="$emit('revoke-admin')"><Icon name="shield" /> {{ tr('Revoke admin') }}</button>
    <div class="row-menu-sep" role="separator"></div>
    <button role="menuitem" class="row-menu-item row-menu-item--danger" @click="$emit('delete')"><Icon name="trash" /> {{ tr('Delete user') }}</button>
  </RowMenu>
</template>
