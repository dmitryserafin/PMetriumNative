// @ts-check
// Note: type annotations allow type checking and IDEs autocompletion

require('dotenv').config()

const lightCodeTheme = require('prism-react-renderer/themes/github');
const darkCodeTheme = require('prism-react-renderer/themes/dracula');

const markdown = import('remark-gfm');
const markdown_plantUML = require("@akebifiky/remark-simple-plantuml");

const configs = {
  PlantUML: {
    baseUrl: process.env.PLANT_UML_URL
  }
};

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'PMetrium Native',
  tagline: '',
  url: 'https://parimatch-tech.github.io',
  baseUrl: '/PMetriumNative/',
  onBrokenLinks: 'throw',
  onBrokenMarkdownLinks: 'throw',
  favicon: 'img/favicon.ico',
  organizationName: 'PM', // Usually your GitHub org/user name.
  projectName: 'PMetrium Native', // Usually your repo name.

  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: { 
          id: 'default',
          path: 'docs/tutorial',
          routeBasePath: 'tutorial',
          sidebarPath: require.resolve('./sidebars.js'),
          //editUrl: '',
          remarkPlugins: [
            markdown,
            require('mdx-mermaid'),
            [markdown_plantUML, { baseUrl: configs.PlantUML.baseUrl }]
          ],
          showLastUpdateAuthor: true,
          showLastUpdateTime: true
        },
        theme: {
          customCss: require.resolve('./src/css/custom.css'),
        },
      }),
    ],
  ],

  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      navbar: {
        title: 'PMetrium Native',
        logo: {
          alt: 'PMetrium Native Logo',
          src: 'img/logo.svg',
        },
        items: [
          {
            type: 'doc',
            docId: 'intro',
            docsPluginId: 'tools-docs',
            position: 'left',
            label: 'Tools',
          },
        ],
      },
      footer: {
        style: 'dark',
        links: [
          {
            title: 'Authors',
            items: [
              {
                label: 'Mykola Panasiuk',
                href: 'https://www.linkedin.com/in/mykola-panasiuk-04ab79128',
              },
              {
                label: 'Pavlo Maikshak',
                href: 'https://www.linkedin.com/in/pavlo-maikshak-29ba12179',
              }
            ],
          },
          {
            title: 'More',
            items: [
              {
                label: 'GitHub',
                href: 'https://github.com/parimatch-tech/PMetriumNative',
              },
            ],
          },
        ],
        copyright: `Copyright © ${new Date().getFullYear()} PMetrium Native Docs, PM. Built with Docusaurus.`,
      },
      prism: {
        theme: lightCodeTheme,
        darkTheme: darkCodeTheme,
        additionalLanguages: ['csharp'],
      },
      tableOfContents: {
        minHeadingLevel: 2,
        maxHeadingLevel: 6,
      },
    }),

  plugins: [
    [
      '@docusaurus/plugin-content-docs',
      {
        id: 'tools-docs',
        path: 'docs/tools',
        routeBasePath: 'tools',
        //editUrl: '',
        remarkPlugins: [
          markdown,
          require('mdx-mermaid'),
          [markdown_plantUML, { baseUrl: configs.PlantUML.baseUrl }]
        ],
        showLastUpdateAuthor: true,
        showLastUpdateTime: true
      },
    ],
  ]
};

module.exports = config;
