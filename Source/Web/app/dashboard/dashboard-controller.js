(function () {
    'use strict';

    angular.module('app.dashboard')
        .controller('Dashboard', ['eventService', 'stackService', function (eventService, stackService) {
            var vm = this;
            vm.mostFrequent = {
                header: 'Most Frequent',
                get: function (options) {
                    return stackService.getAll(options);
                },
                options: {
                    limit: 4,
                    mode: 'summary'
                }
            };

            vm.mostRecent = {
                header: 'Most Recent',
                get: function (options) {
                    return eventService.getAll(options);
                },
                options: {
                    limit: 10,
                    mode: 'summary'
                }
            };
        }
    ]);
}());