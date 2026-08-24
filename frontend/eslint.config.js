import pluginVue from 'eslint-plugin-vue'
import { defineConfigWithVueTs, vueTsConfigs } from '@vue/eslint-config-typescript'
import vuePrettierConfig from '@vue/eslint-config-prettier'
import pluginSonarjs from 'eslint-plugin-sonarjs'

export default defineConfigWithVueTs(
  {
    name: 'app/files-to-lint',
    files: ['**/*.{ts,mts,tsx,vue}'],
  },
  {
    name: 'app/files-to-ignore',
    ignores: ['**/dist/**', '**/dist-ssr/**', '**/coverage/**'],
  },
  pluginVue.configs['flat/recommended'],
  vueTsConfigs.recommended,
  pluginSonarjs.configs.recommended,
  {
    name: 'app/enforce-alias-imports',
    files: ['**/*.{ts,mts,tsx,vue}'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['../*'],
              message:
                "Use the '@/' alias instead of a relative parent import, e.g. '@/services/api'.",
            },
          ],
        },
      ],
    },
  },
  vuePrettierConfig,
)
